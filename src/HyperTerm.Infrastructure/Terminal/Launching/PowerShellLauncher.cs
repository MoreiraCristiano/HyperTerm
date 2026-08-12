using HyperTerm.Core.Abstractions.Settings;
using HyperTerm.Core.Abstractions.Terminal;
using HyperTerm.Core.Entities;
using HyperTerm.Core.Exceptions;
using HyperTerm.Core.Models;
using HyperTerm.Core.Services;
using Microsoft.Extensions.Logging;

namespace HyperTerm.Infrastructure.Terminal;

internal sealed class TerminalSessionFactory(
    ISettingsService settingsService,
    ITerminalProfileResolver profileResolver,
    ILogger<TerminalSessionFactory> logger)
    : ITerminalSessionFactory
{
    public async Task<TerminalSessionDefinition> CreateLocalAsync(
        CancellationToken cancellationToken = default)
    {
        ApplicationSettings settings = await settingsService.LoadAsync(cancellationToken);
        return CreateProfileDefinition(settings, profileId: null);
    }

    public async Task<TerminalSessionDefinition> CreateProfileAsync(
        string profileId,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Preparing a local terminal definition.");
        ApplicationSettings settings = await settingsService.LoadAsync(cancellationToken);
        return CreateProfileDefinition(settings, profileId);
    }

    public Task<TerminalSessionDefinition> CreateAsync(
        Session session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        cancellationToken.ThrowIfCancellationRequested();
        logger.LogInformation("Preparing an SSH terminal definition.");

        string sshPath = WindowsExecutableResolver.Resolve("ssh.exe", "ssh.exe");
        return Task.FromResult(new TerminalSessionDefinition(
            sshPath,
            [
                "-p",
                session.Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
                $"{session.Username}@{session.Host}",
            ],
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            TerminalSessionKind.Ssh));
    }

    private TerminalSessionDefinition CreateProfileDefinition(
        ApplicationSettings settings,
        string? profileId)
    {
        TerminalProfile profile = TerminalProfileCatalog.GetProfile(settings, profileId);
        string executablePath = profileResolver.Resolve(profile.ExecutablePath);
        string startingDirectory = string.IsNullOrWhiteSpace(profile.StartingDirectory)
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : profile.StartingDirectory.Trim().Trim('"');
        return new TerminalSessionDefinition(
            executablePath,
            profile.Arguments,
            startingDirectory)
        {
            ProfileId = profile.Id,
            DisplayName = profile.Name,
        };
    }

}
