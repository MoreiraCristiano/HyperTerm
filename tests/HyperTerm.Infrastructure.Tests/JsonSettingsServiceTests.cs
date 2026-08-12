using HyperTerm.Core.Models;
using HyperTerm.Infrastructure.Settings;
using HyperTerm.Infrastructure.Storage;
using Xunit;

namespace HyperTerm.Infrastructure.Tests;

public sealed class JsonSettingsServiceTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        "HyperTerm.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Terminal_profiles_round_trip_without_argument_flattening()
    {
        var paths = new TestPathProvider(directory);
        using var service = new JsonSettingsService(paths);
        var profile = new TerminalProfile
        {
            Id = "custom",
            Name = "Custom",
            ExecutablePath = "custom.exe",
            Arguments = ["--value", "two words"],
            StartingDirectory = @"C:\Work",
        };
        var settings = new ApplicationSettings
        {
            TerminalProfiles = [profile],
            DefaultTerminalProfileId = profile.Id,
        };

        await service.SaveAsync(settings, TestContext.Current.CancellationToken);
        ApplicationSettings loaded = await service.LoadAsync(TestContext.Current.CancellationToken);

        TerminalProfile actual = Assert.Single(loaded.TerminalProfiles);
        Assert.Equal(profile.Id, actual.Id);
        Assert.Equal(profile.Name, actual.Name);
        Assert.Equal(profile.ExecutablePath, actual.ExecutablePath);
        Assert.Equal(profile.Arguments, actual.Arguments);
        Assert.Equal(profile.StartingDirectory, actual.StartingDirectory);
        Assert.Equal(profile.Id, loaded.DefaultTerminalProfileId);
    }

    [Fact]
    public async Task SaveReplacesExistingSettingsAndLeavesNoTemporaryFile()
    {
        var paths = new TestPathProvider(directory);
        using var service = new JsonSettingsService(paths);
        await service.SaveAsync(
            new ApplicationSettings { PowerShellPath = "powershell.exe" },
            TestContext.Current.CancellationToken);
        await service.SaveAsync(
            new ApplicationSettings { PowerShellPath = "pwsh.exe", TerminalFontSize = 15 },
            TestContext.Current.CancellationToken);

        ApplicationSettings loaded = await service.LoadAsync(
            TestContext.Current.CancellationToken);

        Assert.Equal("pwsh.exe", loaded.PowerShellPath);
        Assert.Equal(15, loaded.TerminalFontSize);
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }

    [Fact]
    public async Task ConcurrentSavesAlwaysProduceCompleteJson()
    {
        var paths = new TestPathProvider(directory);
        using var service = new JsonSettingsService(paths);
        ApplicationSettings first = new() { PowerShellPath = "pwsh.exe" };
        ApplicationSettings second = new() { PowerShellPath = "powershell.exe" };

        await Task.WhenAll(
            service.SaveAsync(first, TestContext.Current.CancellationToken),
            service.SaveAsync(second, TestContext.Current.CancellationToken));

        ApplicationSettings loaded = await service.LoadAsync(
            TestContext.Current.CancellationToken);
        Assert.Contains(loaded.PowerShellPath, new[] { "pwsh.exe", "powershell.exe" });
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class TestPathProvider : IApplicationPathProvider
    {
        public TestPathProvider(string root)
        {
            ApplicationDirectory = root;
            DatabasePath = Path.Combine(root, "test.db");
            SettingsPath = Path.Combine(root, "settings.json");
            LogsDirectory = Path.Combine(root, "logs");
            Directory.CreateDirectory(root);
        }

        public string ApplicationDirectory { get; }
        public string DatabasePath { get; }
        public string SettingsPath { get; }
        public string LogsDirectory { get; }
    }
}
