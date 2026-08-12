using HyperTerm.Core.Exceptions;

namespace HyperTerm.Infrastructure.Terminal;

internal static class WindowsExecutableResolver
{
    public static string? TryResolve(
        string configuredPath,
        string executableName,
        IReadOnlyList<string>? searchDirectories = null)
    {
        string candidate = configuredPath.Trim().Trim('"');
        if (Path.IsPathRooted(candidate))
        {
            return File.Exists(candidate) ? candidate : null;
        }

        IEnumerable<string> directories = searchDirectories ??
            (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        foreach (string directory in directories)
        {
            string fullPath = Path.Combine(directory.Trim(), candidate);
            if (File.Exists(fullPath))
            {
                return fullPath;
            }
        }

        string systemDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System);
        string systemPath = executableName.ToLowerInvariant() switch
        {
            "ssh.exe" => Path.Combine(systemDirectory, "OpenSSH", executableName),
            "powershell.exe" => Path.Combine(
                systemDirectory,
                "WindowsPowerShell",
                "v1.0",
                executableName),
            _ => Path.Combine(systemDirectory, executableName),
        };
        if (File.Exists(systemPath))
        {
            return systemPath;
        }

        if (executableName.Equals("pwsh.exe", StringComparison.OrdinalIgnoreCase))
        {
            string standardPwshPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "PowerShell",
                "7",
                "pwsh.exe");
            if (File.Exists(standardPwshPath))
            {
                return standardPwshPath;
            }
        }

        if (executableName.Equals("bash.exe", StringComparison.OrdinalIgnoreCase))
        {
            string[] programFilesDirectories =
            [
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            ];
            foreach (string programFiles in programFilesDirectories.Distinct(
                         StringComparer.OrdinalIgnoreCase))
            {
                string gitBashPath = Path.Combine(programFiles, "Git", "bin", "bash.exe");
                if (File.Exists(gitBashPath))
                {
                    return gitBashPath;
                }
            }
        }

        return null;
    }

    public static string? TryResolveBundledOrPath(
        string bundledRelativePath,
        string configuredPath,
        string executableName,
        string? applicationDirectory = null,
        IReadOnlyList<string>? searchDirectories = null)
    {
        string bundledPath = Path.GetFullPath(Path.Combine(
            applicationDirectory ?? AppContext.BaseDirectory,
            bundledRelativePath));
        if (File.Exists(bundledPath))
        {
            return bundledPath;
        }

        return TryResolve(configuredPath, executableName, searchDirectories);
    }

    public static string Resolve(string configuredPath, string executableName)
    {
        string candidate = configuredPath.Trim().Trim('"');
        string? resolved = TryResolve(candidate, executableName);
        if (resolved is not null)
        {
            return resolved;
        }

        if (Path.IsPathRooted(candidate))
        {
            throw new TerminalLaunchException($"Executable was not found at ‘{candidate}’.");
        }

        throw new TerminalLaunchException($"‘{candidate}’ was not found in the Windows PATH.");
    }
}
