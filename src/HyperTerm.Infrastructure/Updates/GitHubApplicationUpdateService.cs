using System.IO.Compression;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using HyperTerm.Core.Abstractions.Services;
using HyperTerm.Core.Models;
using HyperTerm.Infrastructure.Storage;

namespace HyperTerm.Infrastructure.Updates;

internal sealed partial class GitHubApplicationUpdateService :
    IApplicationUpdateService,
    IDisposable
{
    private const long MaximumArchiveBytes = 512L * 1024 * 1024;
    private const long MaximumExtractedBytes = 2L * 1024 * 1024 * 1024;
    private const int MaximumArchiveEntries = 10_000;
    private const int MaximumChecksumBytes = 4096;
    private static readonly Uri LatestReleaseUri = new(
        "https://api.github.com/repos/MoreiraCristiano/HyperTerm/releases/latest");

    private readonly IApplicationPathProvider pathProvider;
    private readonly HttpClient httpClient;

    public GitHubApplicationUpdateService(IApplicationPathProvider pathProvider)
        : this(pathProvider, new HttpClientHandler())
    {
    }

    internal GitHubApplicationUpdateService(
        IApplicationPathProvider pathProvider,
        HttpMessageHandler messageHandler)
    {
        this.pathProvider = pathProvider;
        httpClient = new HttpClient(messageHandler, disposeHandler: true);
        httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("HyperTerm", "1.0"));
        httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public async Task<ApplicationUpdateInfo?> CheckAsync(
        Version currentVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(currentVersion);
        await Task.Run(
            () => CleanupInstalledUpdates(currentVersion),
            cancellationToken).ConfigureAwait(false);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));

        try
        {
            using HttpResponseMessage response = await httpClient.GetAsync(
                LatestReleaseUri,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            await using Stream content = await response.Content.ReadAsStreamAsync(timeout.Token)
                .ConfigureAwait(false);
            GitHubRelease? release = await JsonSerializer.DeserializeAsync(
                content,
                UpdateJsonContext.Default.GitHubRelease,
                timeout.Token).ConfigureAwait(false);

            if (release is null || release.Draft || release.Prerelease ||
                !TryParseReleaseVersion(release.TagName, out Version? releaseVersion) ||
                releaseVersion <= NormalizeVersion(currentVersion))
            {
                return null;
            }

            string runtime = GetCurrentRuntime();
            string packageName = $"HyperTerm-{releaseVersion}-{runtime}.zip";
            GitHubReleaseAsset? package = release.Assets?.FirstOrDefault(asset =>
                string.Equals(asset.Name, packageName, StringComparison.Ordinal));
            GitHubReleaseAsset? checksum = release.Assets?.FirstOrDefault(asset =>
                string.Equals(asset.Name, packageName + ".sha256", StringComparison.Ordinal));
            if (package is null || checksum is null)
            {
                throw new InvalidOperationException(
                    $"HyperTerm {releaseVersion} has no complete {runtime} update package.");
            }

            if (!Uri.TryCreate(release.HtmlUrl, UriKind.Absolute, out Uri? releasePageUri) ||
                !Uri.TryCreate(package.BrowserDownloadUrl, UriKind.Absolute, out Uri? packageUri) ||
                !Uri.TryCreate(checksum.BrowserDownloadUrl, UriKind.Absolute, out Uri? checksumUri) ||
                !IsHttps(releasePageUri) || !IsHttps(packageUri) || !IsHttps(checksumUri))
            {
                throw new InvalidDataException("The update release contains an invalid URL.");
            }

            return new ApplicationUpdateInfo(
                releaseVersion!,
                releasePageUri,
                packageUri,
                checksumUri,
                packageName);
        }
        catch (OperationCanceledException) when (
            !cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
        {
            throw new TimeoutException("The update check timed out.");
        }
    }

    public async Task<PreparedApplicationUpdate> PrepareAsync(
        ApplicationUpdateInfo update,
        IProgress<ApplicationUpdateProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        string updateRoot = GetContainedPath(
            pathProvider.UpdatesDirectory,
            update.Version.ToString());
        string stagedDirectory = Path.Combine(updateRoot, "package");
        string stagedExecutable = Path.Combine(stagedDirectory, "HyperTerm.exe");
        if (Directory.Exists(stagedDirectory))
        {
            try
            {
                await Task.Run(
                    () => ValidateExtractedPackage(stagedDirectory, update),
                    cancellationToken).ConfigureAwait(false);
                return new PreparedApplicationUpdate(
                    update.Version,
                    stagedDirectory,
                    stagedExecutable);
            }
            catch (InvalidDataException)
            {
                await Task.Run(
                    () => DeleteDirectoryIfExists(stagedDirectory),
                    cancellationToken).ConfigureAwait(false);
            }
        }

        Directory.CreateDirectory(updateRoot);
        string archivePath = Path.Combine(updateRoot, update.PackageName);
        string partialArchivePath = archivePath + ".download";
        string extractionPath = Path.Combine(updateRoot, "package.extracting");

        DeleteFileIfExists(partialArchivePath);
        DeleteDirectoryIfExists(extractionPath);

        try
        {
            string expectedHash = await DownloadChecksumAsync(
                update,
                cancellationToken).ConfigureAwait(false);
            await DownloadArchiveAsync(
                update.PackageUri,
                partialArchivePath,
                progress,
                cancellationToken).ConfigureAwait(false);

            string actualHash;
            await using (var archive = new FileStream(
                partialArchivePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                byte[] hash = await SHA256.HashDataAsync(archive, cancellationToken)
                    .ConfigureAwait(false);
                actualHash = Convert.ToHexStringLower(hash);
            }

            if (!string.Equals(expectedHash, actualHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The update package SHA-256 does not match.");
            }

            File.Move(partialArchivePath, archivePath, overwrite: true);
            ExtractArchive(archivePath, extractionPath, cancellationToken);
            ValidateExtractedPackage(extractionPath, update);
            Directory.Move(extractionPath, stagedDirectory);
            return new PreparedApplicationUpdate(
                update.Version,
                stagedDirectory,
                stagedExecutable);
        }
        catch
        {
            DeleteFileIfExists(partialArchivePath);
            DeleteDirectoryIfExists(extractionPath);
            throw;
        }
    }

    public void Dispose() => httpClient.Dispose();

    internal static bool TryParseReleaseVersion(string? tag, out Version? version)
    {
        Match match = ReleaseTagRegex().Match(tag ?? string.Empty);
        if (!match.Success ||
            !int.TryParse(match.Groups[1].Value, out int major) ||
            !int.TryParse(match.Groups[2].Value, out int minor) ||
            !int.TryParse(match.Groups[3].Value, out int build))
        {
            version = null;
            return false;
        }

        version = new Version(major, minor, build);
        return true;
    }

    private async Task<string> DownloadChecksumAsync(
        ApplicationUpdateInfo update,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using HttpResponseMessage response = await httpClient.GetAsync(
            update.ChecksumUri,
            HttpCompletionOption.ResponseHeadersRead,
            timeout.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > MaximumChecksumBytes)
        {
            throw new InvalidDataException("The update checksum file is too large.");
        }

        string text = await response.Content.ReadAsStringAsync(timeout.Token)
            .ConfigureAwait(false);
        if (text.Length > MaximumChecksumBytes)
        {
            throw new InvalidDataException("The update checksum file is too large.");
        }

        Match match = ChecksumRegex().Match(text.Trim());
        if (!match.Success ||
            !string.Equals(match.Groups[2].Value, update.PackageName, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The update checksum file is invalid.");
        }

        return match.Groups[1].Value;
    }

    private async Task DownloadArchiveAsync(
        Uri uri,
        string destinationPath,
        IProgress<ApplicationUpdateProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(15));
        using HttpResponseMessage response = await httpClient.GetAsync(
            uri,
            HttpCompletionOption.ResponseHeadersRead,
            timeout.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        long? totalBytes = response.Content.Headers.ContentLength;
        if (totalBytes is > MaximumArchiveBytes)
        {
            throw new InvalidDataException("The update package is too large.");
        }

        await using Stream source = await response.Content.ReadAsStreamAsync(timeout.Token)
            .ConfigureAwait(false);
        await using var destination = new FileStream(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        byte[] buffer = new byte[81920];
        long received = 0;
        while (true)
        {
            int count = await source.ReadAsync(buffer, timeout.Token).ConfigureAwait(false);
            if (count == 0)
            {
                break;
            }

            received += count;
            if (received > MaximumArchiveBytes)
            {
                throw new InvalidDataException("The update package is too large.");
            }

            await destination.WriteAsync(buffer.AsMemory(0, count), timeout.Token)
                .ConfigureAwait(false);
            progress?.Report(new ApplicationUpdateProgress(received, totalBytes));
        }
    }

    private static void ExtractArchive(
        string archivePath,
        string destinationDirectory,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(destinationDirectory);
        string destinationRoot = Path.GetFullPath(destinationDirectory) + Path.DirectorySeparatorChar;
        using ZipArchive archive = ZipFile.OpenRead(archivePath);
        if (archive.Entries.Count > MaximumArchiveEntries)
        {
            throw new InvalidDataException("The update package contains too many files.");
        }

        long extractedBytes = 0;
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            extractedBytes = checked(extractedBytes + entry.Length);
            if (extractedBytes > MaximumExtractedBytes)
            {
                throw new InvalidDataException("The extracted update package is too large.");
            }

            string destinationPath = Path.GetFullPath(
                Path.Combine(destinationDirectory, entry.FullName));
            if (!destinationPath.StartsWith(destinationRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The update package contains an invalid path.");
            }

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(destinationPath);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            entry.ExtractToFile(destinationPath);
        }
    }

    private static void ValidateExtractedPackage(
        string directory,
        ApplicationUpdateInfo update)
    {
        string manifestPath = Path.Combine(directory, "HyperTerm.manifest.json");
        if (!File.Exists(manifestPath))
        {
            throw new InvalidDataException("The update manifest is missing.");
        }

        ReleaseManifest? manifest = JsonSerializer.Deserialize(
            File.ReadAllText(manifestPath),
            UpdateJsonContext.Default.ReleaseManifest);
        string expectedRuntime = GetCurrentRuntime();
        if (manifest is null || manifest.SchemaVersion != 2 ||
            !string.Equals(manifest.Product, "HyperTerm", StringComparison.Ordinal) ||
            !string.Equals(manifest.Version, update.Version.ToString(), StringComparison.Ordinal) ||
            !string.Equals(manifest.Runtime, expectedRuntime, StringComparison.Ordinal) ||
            manifest.Files.Count == 0)
        {
            throw new InvalidDataException("The update manifest is incompatible.");
        }

        string[] actualFiles = Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(directory, path).Replace('\\', '/'))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        string[] manifestFiles = manifest.Files
            .Select(NormalizeManifestPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (!actualFiles.SequenceEqual(manifestFiles, StringComparer.OrdinalIgnoreCase) ||
            !File.Exists(Path.Combine(directory, "HyperTerm.exe")))
        {
            throw new InvalidDataException("The update package does not match its manifest.");
        }
    }

    private static string NormalizeManifestPath(string path)
    {
        string normalized = path.Replace('\\', '/').TrimStart('/');
        if (string.IsNullOrWhiteSpace(normalized) ||
            normalized.Split('/').Any(segment => segment is "" or "." or ".."))
        {
            throw new InvalidDataException("The update manifest contains an invalid path.");
        }

        return normalized;
    }

    private static string GetCurrentRuntime() =>
        RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "win-x64",
            Architecture.Arm64 => "win-arm64",
            _ => throw new PlatformNotSupportedException(
                "Automatic updates require an x64 or ARM64 HyperTerm build."),
        };

    private static Version NormalizeVersion(Version version) =>
        new(version.Major, version.Minor, Math.Max(0, version.Build));

    private static bool IsHttps(Uri uri) =>
        string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);

    private static string GetContainedPath(string root, string name)
    {
        string fullRoot = Path.GetFullPath(root);
        string path = Path.GetFullPath(Path.Combine(fullRoot, name));
        if (!path.StartsWith(fullRoot + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The update path is invalid.");
        }

        return path;
    }

    private void CleanupInstalledUpdates(Version currentVersion)
    {
        if (!Directory.Exists(pathProvider.UpdatesDirectory))
        {
            return;
        }

        Version normalizedCurrentVersion = NormalizeVersion(currentVersion);
        foreach (string directory in Directory.EnumerateDirectories(
            pathProvider.UpdatesDirectory))
        {
            string name = Path.GetFileName(directory);
            if (!Version.TryParse(name, out Version? version) ||
                NormalizeVersion(version) > normalizedCurrentVersion)
            {
                continue;
            }

            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static void DeleteFileIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    [GeneratedRegex("^v(0|[1-9]\\d*)\\.(0|[1-9]\\d*)\\.(0|[1-9]\\d*)$", RegexOptions.CultureInvariant)]
    private static partial Regex ReleaseTagRegex();

    [GeneratedRegex("^([0-9a-fA-F]{64}) {2}([^\\r\\n]+)$", RegexOptions.CultureInvariant)]
    private static partial Regex ChecksumRegex();
}

internal sealed record GitHubRelease(
    [property: System.Text.Json.Serialization.JsonPropertyName("tag_name")] string? TagName,
    [property: System.Text.Json.Serialization.JsonPropertyName("html_url")] string? HtmlUrl,
    [property: System.Text.Json.Serialization.JsonPropertyName("draft")] bool Draft,
    [property: System.Text.Json.Serialization.JsonPropertyName("prerelease")] bool Prerelease,
    [property: System.Text.Json.Serialization.JsonPropertyName("assets")] IReadOnlyList<GitHubReleaseAsset>? Assets);

internal sealed record GitHubReleaseAsset(
    [property: System.Text.Json.Serialization.JsonPropertyName("name")] string Name,
    [property: System.Text.Json.Serialization.JsonPropertyName("browser_download_url")] string BrowserDownloadUrl);

internal sealed record ReleaseManifest(
    [property: System.Text.Json.Serialization.JsonPropertyName("schemaVersion")] int SchemaVersion,
    [property: System.Text.Json.Serialization.JsonPropertyName("product")] string Product,
    [property: System.Text.Json.Serialization.JsonPropertyName("version")] string Version,
    [property: System.Text.Json.Serialization.JsonPropertyName("runtime")] string Runtime,
    [property: System.Text.Json.Serialization.JsonPropertyName("files")] IReadOnlyList<string> Files);

[System.Text.Json.Serialization.JsonSerializable(typeof(GitHubRelease))]
[System.Text.Json.Serialization.JsonSerializable(typeof(ReleaseManifest))]
internal sealed partial class UpdateJsonContext :
    System.Text.Json.Serialization.JsonSerializerContext;
