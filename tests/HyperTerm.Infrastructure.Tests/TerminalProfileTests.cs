using HyperTerm.Core.Abstractions.Settings;
using HyperTerm.Core.Entities;
using HyperTerm.Core.Models;
using HyperTerm.Infrastructure.Terminal;
using Microsoft.Extensions.Logging.Abstractions;

namespace HyperTerm.Infrastructure.Tests;

public sealed class TerminalProfileTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        "HyperTerm.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Factory_creates_default_profile_with_literal_arguments()
    {
        Directory.CreateDirectory(directory);
        string executable = Path.Combine(directory, "shell.exe");
        await File.WriteAllBytesAsync(executable, []);
        var profile = new TerminalProfile
        {
            Id = "custom",
            Name = "Custom shell",
            ExecutablePath = executable,
            Arguments = ["--name", "value with spaces"],
            StartingDirectory = directory,
        };
        var settings = new ApplicationSettings
        {
            TerminalProfiles = [profile],
            DefaultTerminalProfileId = profile.Id,
        };
        var factory = new TerminalSessionFactory(
            new StubSettingsService(settings),
            new TerminalProfileResolver(),
            NullLogger<TerminalSessionFactory>.Instance);

        TerminalSessionDefinition definition = await factory.CreateLocalAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal(executable, definition.Process);
        Assert.Equal(profile.Arguments, definition.Arguments);
        Assert.Equal(directory, definition.StartingDirectory);
        Assert.Equal(profile.Id, definition.ProfileId);
        Assert.Equal(profile.Name, definition.DisplayName);
        Assert.Equal(TerminalSessionKind.Local, definition.Kind);
    }

    [Fact]
    public void Resolver_finds_command_prompt()
    {
        var resolver = new TerminalProfileResolver();

        string? resolved = resolver.TryResolve("cmd.exe");

        Assert.NotNull(resolved);
        Assert.EndsWith("cmd.exe", resolved, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Factory_launches_ssh_directly_without_power_shell()
    {
        var factory = new TerminalSessionFactory(
            new StubSettingsService(new ApplicationSettings
            {
                PowerShellPath = "missing-powershell.exe",
            }),
            new TerminalProfileResolver(),
            NullLogger<TerminalSessionFactory>.Instance);
        var session = new Session(
            Guid.NewGuid(),
            "Server",
            "example.test",
            2222,
            "developer",
            null,
            string.Empty,
            null,
            DateTime.UtcNow,
            DateTime.UtcNow);

        TerminalSessionDefinition definition = await factory.CreateAsync(
            session,
            TestContext.Current.CancellationToken);

        Assert.EndsWith("ssh.exe", definition.Process, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(["-p", "2222", "developer@example.test"], definition.Arguments);
        Assert.Equal(TerminalSessionKind.Ssh, definition.Kind);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class StubSettingsService(ApplicationSettings settings) : ISettingsService
    {
        public bool Exists() => true;

        public Task<ApplicationSettings> LoadAsync(
            CancellationToken cancellationToken = default) => Task.FromResult(settings);

        public Task SaveAsync(
            ApplicationSettings value,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
