using System.IO.Compression;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HyperTerm.Infrastructure.Storage;
using HyperTerm.Infrastructure.Updates;

namespace HyperTerm.Infrastructure.Tests;

public sealed class ApplicationUpdateServiceTests
{
    [Theory]
    [InlineData("v1.2.3", 1, 2, 3)]
    [InlineData("1.2.3", -1, -1, -1)]
    [InlineData("v1.2.3-beta", -1, -1, -1)]
    [InlineData("v01.2.3", -1, -1, -1)]
    public void Release_tags_require_strict_semantic_versions(
        string tag,
        int major,
        int minor,
        int build)
    {
        bool parsed = GitHubApplicationUpdateService.TryParseReleaseVersion(
            tag,
            out Version? version);

        Assert.Equal(major >= 0, parsed);
        if (parsed)
        {
            Assert.Equal(new Version(major, minor, build), version);
        }
    }

    [Fact]
    public async Task Check_returns_newer_release_with_matching_runtime_assets()
    {
        using var paths = new UpdateTestPaths();
        string runtime = RuntimeInformation.ProcessArchitecture == Architecture.Arm64
            ? "win-arm64"
            : "win-x64";
        string packageName = $"HyperTerm-2.1.0-{runtime}.zip";
        var handler = new StubHttpMessageHandler(request =>
        {
            Assert.Contains("releases/latest", request.RequestUri!.AbsoluteUri);
            string json = JsonSerializer.Serialize(new
            {
                tag_name = "v2.1.0",
                html_url = "https://github.test/releases/v2.1.0",
                draft = false,
                prerelease = false,
                assets = new[]
                {
                    new { name = packageName, browser_download_url = "https://download.test/package" },
                    new { name = packageName + ".sha256", browser_download_url = "https://download.test/checksum" },
                },
            });
            return JsonResponse(json);
        });
        using var service = new GitHubApplicationUpdateService(paths, handler);

        HyperTerm.Core.Models.ApplicationUpdateInfo? update = await service.CheckAsync(
            new Version(2, 0, 9),
            TestContext.Current.CancellationToken);

        Assert.NotNull(update);
        Assert.Equal(new Version(2, 1, 0), update.Version);
        Assert.Equal(packageName, update.PackageName);
    }

    [Fact]
    public async Task Check_removes_only_installed_update_cache_versions()
    {
        using var paths = new UpdateTestPaths();
        string installedCache = Path.Combine(paths.UpdatesDirectory, "1.0.0");
        string futureCache = Path.Combine(paths.UpdatesDirectory, "3.0.0");
        Directory.CreateDirectory(installedCache);
        Directory.CreateDirectory(futureCache);
        var handler = new StubHttpMessageHandler(_ => JsonResponse(JsonSerializer.Serialize(new
        {
            tag_name = "v2.0.0",
            html_url = "https://github.test/releases/v2.0.0",
            draft = false,
            prerelease = false,
            assets = Array.Empty<object>(),
        })));
        using var service = new GitHubApplicationUpdateService(paths, handler);

        HyperTerm.Core.Models.ApplicationUpdateInfo? update = await service.CheckAsync(
            new Version(2, 0, 0),
            TestContext.Current.CancellationToken);

        Assert.Null(update);
        Assert.False(Directory.Exists(installedCache));
        Assert.True(Directory.Exists(futureCache));
    }

    [Fact]
    public async Task Prepare_validates_checksum_manifest_and_extracts_package()
    {
        using var paths = new UpdateTestPaths();
        Version version = new(3, 2, 1);
        byte[] archive = CreateReleaseArchive(version);
        string packageName = $"HyperTerm-{version}-{CurrentRuntime()}.zip";
        string checksum = Convert.ToHexStringLower(SHA256.HashData(archive));
        var handler = new StubHttpMessageHandler(request =>
            request.RequestUri!.AbsolutePath.EndsWith(".sha256", StringComparison.Ordinal)
                ? TextResponse($"{checksum}  {packageName}")
                : BinaryResponse(archive));
        using var service = new GitHubApplicationUpdateService(paths, handler);
        var update = new HyperTerm.Core.Models.ApplicationUpdateInfo(
            version,
            new Uri("https://github.test/release"),
            new Uri("https://download.test/package.zip"),
            new Uri("https://download.test/package.zip.sha256"),
            packageName);

        HyperTerm.Core.Models.PreparedApplicationUpdate prepared = await service.PrepareAsync(
            update,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(File.Exists(prepared.ExecutablePath));
        Assert.True(File.Exists(Path.Combine(
            prepared.StagingDirectory,
            "HyperTerm.manifest.json")));
    }

    [Fact]
    public async Task Prepare_rejects_checksum_mismatch()
    {
        using var paths = new UpdateTestPaths();
        Version version = new(3, 2, 1);
        byte[] archive = CreateReleaseArchive(version);
        string packageName = $"HyperTerm-{version}-{CurrentRuntime()}.zip";
        var handler = new StubHttpMessageHandler(request =>
            request.RequestUri!.AbsolutePath.EndsWith(".sha256", StringComparison.Ordinal)
                ? TextResponse($"{new string('0', 64)}  {packageName}")
                : BinaryResponse(archive));
        using var service = new GitHubApplicationUpdateService(paths, handler);
        var update = new HyperTerm.Core.Models.ApplicationUpdateInfo(
            version,
            new Uri("https://github.test/release"),
            new Uri("https://download.test/package.zip"),
            new Uri("https://download.test/package.zip.sha256"),
            packageName);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.PrepareAsync(update, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("SHA-256", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Prepare_rejects_archive_path_traversal()
    {
        using var paths = new UpdateTestPaths();
        Version version = new(3, 2, 1);
        byte[] archive;
        using (var stream = new MemoryStream())
        {
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            {
                ZipArchiveEntry entry = zip.CreateEntry("../outside.txt");
                using StreamWriter writer = new(entry.Open());
                writer.Write("bad");
            }

            archive = stream.ToArray();
        }

        string packageName = $"HyperTerm-{version}-{CurrentRuntime()}.zip";
        string checksum = Convert.ToHexStringLower(SHA256.HashData(archive));
        var handler = new StubHttpMessageHandler(request =>
            request.RequestUri!.AbsolutePath.EndsWith(".sha256", StringComparison.Ordinal)
                ? TextResponse($"{checksum}  {packageName}")
                : BinaryResponse(archive));
        using var service = new GitHubApplicationUpdateService(paths, handler);
        var update = new HyperTerm.Core.Models.ApplicationUpdateInfo(
            version,
            new Uri("https://github.test/release"),
            new Uri("https://download.test/package.zip"),
            new Uri("https://download.test/package.zip.sha256"),
            packageName);

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.PrepareAsync(update, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("invalid path", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(Path.Combine(paths.ApplicationDirectory, "outside.txt")));
    }

    private static byte[] CreateReleaseArchive(Version version)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "HyperTerm.exe", "executable");
            string manifest = JsonSerializer.Serialize(new
            {
                schemaVersion = 2,
                product = "HyperTerm",
                version = version.ToString(),
                runtime = CurrentRuntime(),
                files = new[] { "HyperTerm.exe", "HyperTerm.manifest.json" },
            });
            WriteEntry(archive, "HyperTerm.manifest.json", manifest);
        }

        return stream.ToArray();
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        ZipArchiveEntry entry = archive.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.Write(content);
    }

    private static string CurrentRuntime() =>
        RuntimeInformation.ProcessArchitecture == Architecture.Arm64
            ? "win-arm64"
            : "win-x64";

    private static HttpResponseMessage JsonResponse(string content) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json"),
        };

    private static HttpResponseMessage TextResponse(string content) =>
        new(HttpStatusCode.OK) { Content = new StringContent(content) };

    private static HttpResponseMessage BinaryResponse(byte[] content) =>
        new(HttpStatusCode.OK) { Content = new ByteArrayContent(content) };

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(responseFactory(request));
        }
    }

    private sealed class UpdateTestPaths : IApplicationPathProvider, IDisposable
    {
        public UpdateTestPaths()
        {
            ApplicationDirectory = Path.Combine(
                Path.GetTempPath(),
                "HyperTerm.UpdateTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(ApplicationDirectory);
        }

        public string ApplicationDirectory { get; }
        public string DatabasePath => Path.Combine(ApplicationDirectory, "test.db");
        public string SettingsPath => Path.Combine(ApplicationDirectory, "settings.json");
        public string LogsDirectory => Path.Combine(ApplicationDirectory, "logs");
        public string UpdatesDirectory => Path.Combine(ApplicationDirectory, "updates");

        public void Dispose()
        {
            if (Directory.Exists(ApplicationDirectory))
            {
                Directory.Delete(ApplicationDirectory, recursive: true);
            }
        }
    }
}
