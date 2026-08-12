using HyperTerm.Core.Models;
using HyperTerm.Core.Services;

namespace HyperTerm.Core.Tests;

public sealed class TerminalProfileCatalogTests
{
    [Fact]
    public void Legacy_settings_receive_only_recommended_power_shell()
    {
        var settings = new ApplicationSettings { PowerShellPath = "powershell.exe" };

        ApplicationSettings normalized = TerminalProfileCatalog.Normalize(settings);

        TerminalProfile profile = Assert.Single(normalized.TerminalProfiles);
        Assert.Equal(TerminalProfileIds.PowerShell, normalized.DefaultTerminalProfileId);
        Assert.Equal("PowerShell", profile.Name);
        Assert.Equal("powershell.exe", profile.ExecutablePath);
        Assert.Equal(["-NoLogo"], profile.Arguments);
    }

    [Fact]
    public void Existing_profiles_and_custom_default_are_preserved()
    {
        var custom = new TerminalProfile
        {
            Id = "custom",
            Name = "Custom shell",
            ExecutablePath = "custom.exe",
            Arguments = ["--interactive"],
        };
        var settings = new ApplicationSettings
        {
            TerminalProfiles = [custom],
            DefaultTerminalProfileId = custom.Id,
        };

        ApplicationSettings normalized = TerminalProfileCatalog.Normalize(settings);

        Assert.Equal(custom.Id, normalized.DefaultTerminalProfileId);
        Assert.Equal(custom, Assert.Single(normalized.TerminalProfiles));
    }

    [Fact]
    public void Missing_default_falls_back_to_first_persisted_profile()
    {
        var settings = new ApplicationSettings
        {
            TerminalProfiles =
            [
                new TerminalProfile
                {
                    Id = "custom",
                    Name = "Custom shell",
                    ExecutablePath = "custom.exe",
                },
            ],
            DefaultTerminalProfileId = "missing",
        };

        ApplicationSettings normalized = TerminalProfileCatalog.Normalize(settings);

        Assert.Equal("custom", normalized.DefaultTerminalProfileId);
    }

    [Fact]
    public void Power_shell_profile_name_remains_editable()
    {
        var settings = new ApplicationSettings
        {
            TerminalProfiles =
            [
                new TerminalProfile
                {
                    Id = "POWERSHELL",
                    Name = "Renamed",
                    ExecutablePath = "powershell.exe",
                },
            ],
            DefaultTerminalProfileId = "POWERSHELL",
        };

        ApplicationSettings normalized = TerminalProfileCatalog.Normalize(settings);
        TerminalProfile powerShell = normalized.TerminalProfiles[0];

        Assert.Equal("POWERSHELL", powerShell.Id);
        Assert.Equal("Renamed", powerShell.Name);
        Assert.Equal("POWERSHELL", normalized.DefaultTerminalProfileId);
    }

    [Fact]
    public void Explicit_command_prompt_and_git_bash_profiles_are_preserved()
    {
        TerminalProfile[] profiles =
        [
            new TerminalProfile
            {
                Id = "command-prompt",
                Name = "Command Prompt",
                ExecutablePath = "cmd.exe",
            },
            new TerminalProfile
            {
                Id = "git-bash",
                Name = "Git Bash",
                ExecutablePath = "bash.exe",
            },
        ];
        var settings = new ApplicationSettings
        {
            TerminalProfiles = profiles,
            DefaultTerminalProfileId = profiles[0].Id,
        };

        ApplicationSettings normalized = TerminalProfileCatalog.Normalize(settings);

        Assert.Equal(profiles, normalized.TerminalProfiles);
    }
}
