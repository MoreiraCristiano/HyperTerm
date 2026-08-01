using System.Text;
using HyperTerm.Core.Abstractions.Settings;
using HyperTerm.Core.Abstractions.Terminal;
using HyperTerm.Core.Entities;
using HyperTerm.Core.Exceptions;
using HyperTerm.Core.Models;

namespace HyperTerm.Infrastructure.Terminal;

internal sealed class PowerShellSessionFactory(ISettingsService settingsService)
    : ITerminalSessionFactory
{
    public async Task<TerminalSessionDefinition> CreateLocalAsync(
        CancellationToken cancellationToken = default)
    {
        ApplicationSettings settings = await settingsService.LoadAsync(cancellationToken);
        string configuredPath = NormalizePowerShellPath(settings.PowerShellPath);
        string powerShellPath = ResolveExecutable(
            configuredPath,
            Path.GetFileName(configuredPath));

        return new TerminalSessionDefinition(
            powerShellPath,
            ["-NoLogo"],
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
    }

    public async Task<TerminalSessionDefinition> CreateAsync(
        Session session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        cancellationToken.ThrowIfCancellationRequested();

        ApplicationSettings settings = await settingsService.LoadAsync(cancellationToken);
        string configuredPath = NormalizePowerShellPath(settings.PowerShellPath);
        string powerShellPath = ResolveExecutable(
            configuredPath,
            Path.GetFileName(configuredPath));
        string sshPath = ResolveExecutable("ssh.exe", "ssh.exe");
        string command = BuildSshCommand(sshPath, session);
        string encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));

        return new TerminalSessionDefinition(
            powerShellPath,
            ["-NoLogo", "-NoExit", "-EncodedCommand", encodedCommand],
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
    }

    private static string BuildSshCommand(string sshPath, Session session)
    {
        var arguments = new List<string>
        {
            "-p",
            session.Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };

        if (!string.IsNullOrWhiteSpace(session.PrivateKey))
        {
            arguments.Add("-i");
            arguments.Add(session.PrivateKey);
        }

        arguments.Add($"{session.Username}@{session.Host}");
        return $"& {QuotePowerShellLiteral(sshPath)} " +
               string.Join(' ', arguments.Select(QuotePowerShellLiteral));
    }

    private static string QuotePowerShellLiteral(string value) =>
        $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";

    private static string NormalizePowerShellPath(string configuredPath) =>
        string.IsNullOrWhiteSpace(configuredPath)
            ? "pwsh.exe"
            : configuredPath.Trim().Trim('"');

    private static string ResolveExecutable(string configuredPath, string executableName)
    {
        string candidate = configuredPath.Trim().Trim('"');
        if (Path.IsPathRooted(candidate))
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }

            throw new TerminalLaunchException($"Executable was not found at ‘{candidate}’.");
        }

        string? pathEnvironment = Environment.GetEnvironmentVariable("PATH");
        foreach (string directory in (pathEnvironment ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
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

        if (executableName == "pwsh.exe")
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

        throw new TerminalLaunchException($"‘{candidate}’ was not found in the Windows PATH.");
    }
}
