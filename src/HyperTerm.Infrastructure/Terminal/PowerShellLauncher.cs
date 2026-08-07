using System.Text;
using HyperTerm.Core.Abstractions.Settings;
using HyperTerm.Core.Abstractions.Terminal;
using HyperTerm.Core.Entities;
using HyperTerm.Core.Exceptions;
using HyperTerm.Core.Models;
using Microsoft.Extensions.Logging;

namespace HyperTerm.Infrastructure.Terminal;

internal sealed class PowerShellSessionFactory(
    ISettingsService settingsService,
    ILogger<PowerShellSessionFactory> logger)
    : ITerminalSessionFactory
{
    public async Task<TerminalSessionDefinition> CreateLocalAsync(
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Preparing a local terminal definition.");
        ApplicationSettings settings = await settingsService.LoadAsync(cancellationToken);
        string configuredPath = NormalizePowerShellPath(settings.PowerShellPath);
        string powerShellPath = WindowsExecutableResolver.Resolve(
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
        logger.LogInformation("Preparing an SSH terminal definition.");

        ApplicationSettings settings = await settingsService.LoadAsync(cancellationToken);
        string configuredPath = NormalizePowerShellPath(settings.PowerShellPath);
        string powerShellPath = WindowsExecutableResolver.Resolve(
            configuredPath,
            Path.GetFileName(configuredPath));
        string sshPath = WindowsExecutableResolver.Resolve("ssh.exe", "ssh.exe");
        string command = BuildSshCommand(sshPath, session);
        string encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));

        return new TerminalSessionDefinition(
            powerShellPath,
            ["-NoLogo", "-NoExit", "-EncodedCommand", encodedCommand],
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            TerminalSessionKind.Ssh);
    }

    private static string BuildSshCommand(string sshPath, Session session)
    {
        var arguments = new List<string>
        {
            "-p",
            session.Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };

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

}
