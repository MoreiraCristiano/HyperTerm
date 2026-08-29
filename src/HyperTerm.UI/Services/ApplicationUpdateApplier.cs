using System.Diagnostics;
using System.Text.Json;

namespace HyperTerm.UI.Services;

internal sealed record ApplicationUpdateRequest(string TargetDirectory, int ProcessId);

internal static class ApplicationUpdateApplier
{
    private const string ApplyUpdateArgument = "--apply-update";

    public static bool IsUpdateInvocation(string[] args) =>
        args.Length > 0 && string.Equals(
            args[0],
            ApplyUpdateArgument,
            StringComparison.Ordinal);

    public static async Task<int> RunAsync(string[] args)
    {
        string sourceDirectory = Path.GetFullPath(AppContext.BaseDirectory);
        try
        {
            ApplicationUpdateRequest request = ParseArguments(args);
            await WaitForProcessAsync(request.ProcessId).ConfigureAwait(false);
            await ApplyAsync(sourceDirectory, request.TargetDirectory).ConfigureAwait(false);
            LaunchApplication(request.TargetDirectory);
            return 0;
        }
        catch (Exception exception)
        {
            TryWriteFailure(sourceDirectory, exception);
            TryLaunchExistingApplication(args);
            return 1;
        }
    }

    internal static ApplicationUpdateRequest ParseArguments(string[] args)
    {
        if (!IsUpdateInvocation(args))
        {
            throw new ArgumentException("The update invocation is missing.", nameof(args));
        }

        string? target = null;
        int? processId = null;
        for (int index = 1; index < args.Length; index += 2)
        {
            if (index + 1 >= args.Length)
            {
                throw new ArgumentException("The update arguments are incomplete.", nameof(args));
            }

            switch (args[index])
            {
                case "--target":
                    target = args[index + 1];
                    break;
                case "--wait-pid" when int.TryParse(
                    args[index + 1],
                    System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out int value) && value > 0:
                    processId = value;
                    break;
                default:
                    throw new ArgumentException("The update arguments are invalid.", nameof(args));
            }
        }

        if (string.IsNullOrWhiteSpace(target) || processId is null)
        {
            throw new ArgumentException("The update arguments are incomplete.", nameof(args));
        }

        string targetDirectory = Path.GetFullPath(target);
        if (!File.Exists(Path.Combine(targetDirectory, "HyperTerm.exe")))
        {
            throw new InvalidOperationException("The target HyperTerm installation is invalid.");
        }

        return new ApplicationUpdateRequest(targetDirectory, processId.Value);
    }

    internal static async Task ApplyAsync(
        string sourceDirectory,
        string targetDirectory,
        CancellationToken cancellationToken = default)
    {
        string sourceRoot = NormalizeRoot(sourceDirectory);
        string targetRoot = NormalizeRoot(targetDirectory);
        if (string.Equals(sourceRoot, targetRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Update source and target must differ.");
        }

        UpdateFileManifest sourceManifest = ReadManifest(sourceRoot, requireFiles: true);
        UpdateFileManifest targetManifest = ReadManifest(targetRoot, requireFiles: false);
        if (!string.Equals(sourceManifest.Product, "HyperTerm", StringComparison.Ordinal) ||
            !string.Equals(targetManifest.Product, "HyperTerm", StringComparison.Ordinal))
        {
            throw new InvalidDataException("The update manifest product is invalid.");
        }

        string backupRoot = Path.Combine(sourceRoot, ".update-backup");
        if (Directory.Exists(backupRoot))
        {
            Directory.Delete(backupRoot, recursive: true);
        }

        Directory.CreateDirectory(backupRoot);
        var createdFiles = new List<string>();
        var backedUpFiles = new List<string>();
        try
        {
            foreach (string relativePath in sourceManifest.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string sourcePath = GetContainedFile(sourceRoot, relativePath);
                string targetPath = GetContainedFile(targetRoot, relativePath);
                if (!File.Exists(sourcePath))
                {
                    throw new InvalidDataException(
                        $"Update source file is missing: {relativePath}");
                }

                if (File.Exists(targetPath))
                {
                    BackupFile(targetPath, backupRoot, relativePath);
                    backedUpFiles.Add(relativePath);
                }
                else
                {
                    createdFiles.Add(relativePath);
                }

                ReplaceFile(sourcePath, targetPath);
            }

            HashSet<string> newFiles = sourceManifest.Files.ToHashSet(
                StringComparer.OrdinalIgnoreCase);
            foreach (string relativePath in targetManifest.Files.Where(path =>
                !newFiles.Contains(path)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string targetPath = GetContainedFile(targetRoot, relativePath);
                if (!File.Exists(targetPath))
                {
                    continue;
                }

                BackupFile(targetPath, backupRoot, relativePath);
                backedUpFiles.Add(relativePath);
                File.SetAttributes(targetPath, FileAttributes.Normal);
                File.Delete(targetPath);
            }
        }
        catch
        {
            RollBack(targetRoot, backupRoot, backedUpFiles, createdFiles);
            throw;
        }

        try
        {
            Directory.Delete(backupRoot, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        await Task.CompletedTask;
    }

    private static async Task WaitForProcessAsync(int processId)
    {
        if (processId == Environment.ProcessId)
        {
            throw new InvalidOperationException("The updater cannot wait for itself.");
        }

        try
        {
            using Process process = Process.GetProcessById(processId);
            await process.WaitForExitAsync().ConfigureAwait(false);
        }
        catch (ArgumentException)
        {
        }
    }

    private static UpdateFileManifest ReadManifest(string root, bool requireFiles)
    {
        string path = Path.Combine(root, "HyperTerm.manifest.json");
        if (!File.Exists(path))
        {
            throw new InvalidDataException("The HyperTerm manifest is missing.");
        }

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement rootElement = document.RootElement;
        string product = rootElement.GetProperty("product").GetString() ?? string.Empty;
        var files = new List<string>();
        if (rootElement.TryGetProperty("files", out JsonElement fileElements))
        {
            foreach (JsonElement element in fileElements.EnumerateArray())
            {
                files.Add(NormalizeRelativePath(element.GetString() ?? string.Empty));
            }
        }

        if (requireFiles && files.Count == 0)
        {
            throw new InvalidDataException("The update manifest has no managed files.");
        }

        if (files.Count != files.Distinct(StringComparer.OrdinalIgnoreCase).Count())
        {
            throw new InvalidDataException("The update manifest contains duplicate files.");
        }

        return new UpdateFileManifest(product, files);
    }

    private static string NormalizeRelativePath(string path)
    {
        string normalized = path.Replace('\\', '/').TrimStart('/');
        if (string.IsNullOrWhiteSpace(normalized) || Path.IsPathRooted(normalized) ||
            normalized.Split('/').Any(segment => segment is "" or "." or ".."))
        {
            throw new InvalidDataException("The update manifest contains an invalid path.");
        }

        return normalized;
    }

    private static string NormalizeRoot(string directory) =>
        Path.GetFullPath(directory).TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);

    private static string GetContainedFile(string root, string relativePath)
    {
        string normalized = NormalizeRelativePath(relativePath);
        string path = Path.GetFullPath(Path.Combine(root, normalized));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The update manifest contains an invalid path.");
        }

        return path;
    }

    private static void BackupFile(string path, string backupRoot, string relativePath)
    {
        string backupPath = GetContainedFile(backupRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
        File.Copy(path, backupPath, overwrite: true);
    }

    private static void ReplaceFile(string sourcePath, string targetPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        string temporaryPath = targetPath + ".update-new";
        if (File.Exists(temporaryPath))
        {
            File.SetAttributes(temporaryPath, FileAttributes.Normal);
            File.Delete(temporaryPath);
        }

        File.Copy(sourcePath, temporaryPath);
        if (File.Exists(targetPath))
        {
            File.SetAttributes(targetPath, FileAttributes.Normal);
        }

        File.Move(temporaryPath, targetPath, overwrite: true);
    }

    private static void RollBack(
        string targetRoot,
        string backupRoot,
        IEnumerable<string> backedUpFiles,
        IEnumerable<string> createdFiles)
    {
        foreach (string relativePath in createdFiles.Reverse())
        {
            string targetPath = GetContainedFile(targetRoot, relativePath);
            if (File.Exists(targetPath))
            {
                File.SetAttributes(targetPath, FileAttributes.Normal);
                File.Delete(targetPath);
            }
        }

        foreach (string relativePath in backedUpFiles.Distinct(
            StringComparer.OrdinalIgnoreCase).Reverse())
        {
            string backupPath = GetContainedFile(backupRoot, relativePath);
            string targetPath = GetContainedFile(targetRoot, relativePath);
            if (File.Exists(backupPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                if (File.Exists(targetPath))
                {
                    File.SetAttributes(targetPath, FileAttributes.Normal);
                }

                File.Copy(backupPath, targetPath, overwrite: true);
            }
        }
    }

    private static void LaunchApplication(string targetDirectory)
    {
        string executable = Path.Combine(targetDirectory, "HyperTerm.exe");
        var startInfo = new ProcessStartInfo("explorer.exe")
        {
            UseShellExecute = true,
        };
        startInfo.ArgumentList.Add(executable);
        Process.Start(startInfo)?.Dispose();
    }

    private static void TryLaunchExistingApplication(string[] args)
    {
        try
        {
            ApplicationUpdateRequest request = ParseArguments(args);
            LaunchApplication(request.TargetDirectory);
        }
        catch
        {
        }
    }

    private static void TryWriteFailure(string sourceDirectory, Exception exception)
    {
        try
        {
            File.WriteAllText(
                Path.Combine(sourceDirectory, "update-error.txt"),
                $"{DateTimeOffset.UtcNow:O}{Environment.NewLine}{exception}");
        }
        catch
        {
        }
    }

    private sealed record UpdateFileManifest(string Product, IReadOnlyList<string> Files);
}
