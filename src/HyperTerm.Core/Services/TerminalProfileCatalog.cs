using HyperTerm.Core.Models;

namespace HyperTerm.Core.Services;

public static class TerminalProfileCatalog
{
    public static ApplicationSettings Normalize(ApplicationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var configuredProfiles = (settings.TerminalProfiles ?? [])
            .Where(profile => profile is not null && !string.IsNullOrWhiteSpace(profile.Id))
            .Select(profile => profile with
            {
                Name = profile.Name ?? string.Empty,
                ExecutablePath = profile.ExecutablePath ?? string.Empty,
                Arguments = profile.Arguments ?? [],
                StartingDirectory = profile.StartingDirectory ?? string.Empty,
            })
            .GroupBy(profile => profile.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        var profiles = configuredProfiles.Count > 0
            ? configuredProfiles
            :
            [
                new TerminalProfile
                {
                    Id = TerminalProfileIds.PowerShell,
                    Name = "PowerShell",
                    ExecutablePath = NormalizePowerShellPath(settings.PowerShellPath),
                    Arguments = ["-NoLogo"],
                },
            ];
        string defaultId = profiles.FirstOrDefault(profile => profile.Id.Equals(
                settings.DefaultTerminalProfileId,
                StringComparison.OrdinalIgnoreCase))?.Id
            ?? profiles[0].Id;

        return settings with
        {
            TerminalProfiles = profiles,
            DefaultTerminalProfileId = defaultId,
        };
    }

    public static TerminalProfile GetProfile(ApplicationSettings settings, string? profileId = null)
    {
        ApplicationSettings normalized = Normalize(settings);
        string id = string.IsNullOrWhiteSpace(profileId)
            ? normalized.DefaultTerminalProfileId
            : profileId;
        return normalized.TerminalProfiles.FirstOrDefault(profile => profile.Id.Equals(
                   id,
                   StringComparison.OrdinalIgnoreCase))
               ?? throw new KeyNotFoundException($"Terminal profile ‘{id}’ was not found.");
    }

    private static string NormalizePowerShellPath(string path) =>
        string.IsNullOrWhiteSpace(path) ? "pwsh.exe" : path.Trim().Trim('"');
}
