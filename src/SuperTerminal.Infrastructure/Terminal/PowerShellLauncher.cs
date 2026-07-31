using System.Text;
using SuperTerminal.Core.Abstractions.Settings;
using SuperTerminal.Core.Abstractions.Terminal;
using SuperTerminal.Core.Entities;
using SuperTerminal.Core.Exceptions;
using SuperTerminal.Core.Models;

namespace SuperTerminal.Infrastructure.Terminal;

internal sealed class PowerShellSessionFactory(ISettingsService settingsService)
    : ITerminalSessionFactory
{
    public async Task<TerminalSessionDefinition> CreateLocalAsync(
        CancellationToken cancellationToken = default)
    {
        ApplicationSettings settings = await settingsService.LoadAsync(cancellationToken);
        string powerShellPath = ResolveExecutable(
            NormalizeLegacyPath(settings.PowerShellPath),
            "pwsh.exe");

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
        string powerShellPath = ResolveExecutable(
            NormalizeLegacyPath(settings.PowerShellPath),
            "pwsh.exe");
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

    private static string NormalizeLegacyPath(string configuredPath) =>
        string.IsNullOrWhiteSpace(configuredPath) ||
        configuredPath.Equals("powershell.exe", StringComparison.OrdinalIgnoreCase)
            ? "pwsh.exe"
            : configuredPath;

    private static string ResolveExecutable(string configuredPath, string executableName)
    {
        string candidate = configuredPath.Trim().Trim('"');
        if (Path.IsPathRooted(candidate))
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }

            throw new TerminalLaunchException($"Executável não encontrado em ‘{candidate}’.");
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

        string systemPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            executableName == "ssh.exe" ? "OpenSSH" : string.Empty,
            executableName);
        if (File.Exists(systemPath))
        {
            return systemPath;
        }

        if (executableName == "pwsh.exe")
        {
            string powerShell7Path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "PowerShell",
                "7",
                "pwsh.exe");
            if (File.Exists(powerShell7Path))
            {
                return powerShell7Path;
            }
        }

        throw new TerminalLaunchException($"‘{candidate}’ não foi encontrado no PATH do Windows.");
    }
}
