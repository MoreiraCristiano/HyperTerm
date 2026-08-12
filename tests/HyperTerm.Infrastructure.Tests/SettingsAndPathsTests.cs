using System.Text.Json;
using HyperTerm.Core.Models;
using HyperTerm.Infrastructure.Settings;
using HyperTerm.Infrastructure.Storage;

namespace HyperTerm.Infrastructure.Tests;

[CollectionDefinition("Environment", DisableParallelization = true)]
public sealed class EnvironmentCollection;

[Collection("Environment")]
public sealed class ApplicationPathProviderTests
{
    [Fact]
    public void Test_mode_requires_an_explicit_root()
    {
        using var environment = new EnvironmentScope("HYPERTERM_TEST_MODE", "1");
        using var root = new EnvironmentScope("HYPERTERM_DATA_ROOT", null);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => new ApplicationPathProvider());

        Assert.Contains("HYPERTERM_DATA_ROOT", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Test_mode_isolates_all_application_files()
    {
        string directory = Path.Combine(Path.GetTempPath(), "HyperTerm.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            using var environment = new EnvironmentScope("HYPERTERM_TEST_MODE", "1");
            using var root = new EnvironmentScope("HYPERTERM_DATA_ROOT", directory);

            var provider = new ApplicationPathProvider();

            Assert.Equal(Path.GetFullPath(directory), provider.ApplicationDirectory);
            Assert.Equal(Path.Combine(directory, "hyperterm.db"), provider.DatabasePath);
            Assert.Equal(Path.Combine(directory, "settings.json"), provider.SettingsPath);
            Assert.Equal(Path.Combine(directory, "logs"), provider.LogsDirectory);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}

public sealed class JsonSettingsIntegrationTests
{
    [Fact]
    public async Task ExistingSettingsDefaultToKeepingPsmuxSessionsOnExit()
    {
        using var paths = new TemporaryPaths();
        await File.WriteAllTextAsync(
            paths.SettingsPath,
            """{"PowerShellPath":"pwsh.exe"}""");
        using var service = new JsonSettingsService(paths);

        ApplicationSettings settings = await service.LoadAsync();

        Assert.True(settings.KeepPsmuxSessionsOnExit);
        Assert.False(settings.CloseToSystemTray);
    }

    [Fact]
    public async Task Missing_file_returns_defaults_and_save_round_trips()
    {
        using var paths = new TemporaryPaths();
        using var service = new JsonSettingsService(paths);
        Assert.False(service.Exists());
        Assert.Equal(new ApplicationSettings(), await service.LoadAsync());

        var expected = new ApplicationSettings
        {
            Theme = "Light",
            TerminalFontSize = 17,
            CloseToSystemTray = true,
            KeepPsmuxSessionsOnExit = false,
            Window = new WindowSettings { Width = 900, Height = 600, X = 10, Y = 20 },
        };
        await service.SaveAsync(expected);

        Assert.True(service.Exists());
        ApplicationSettings loaded = await service.LoadAsync();
        Assert.Equal(expected with { TerminalProfiles = loaded.TerminalProfiles }, loaded);
        Assert.Empty(loaded.TerminalProfiles);
        Assert.Empty(Directory.EnumerateFiles(paths.ApplicationDirectory, "*.tmp"));
    }

    [Fact]
    public async Task Corrupt_json_is_reported_without_overwriting_file()
    {
        using var paths = new TemporaryPaths();
        await File.WriteAllTextAsync(paths.SettingsPath, "{not-json");
        using var service = new JsonSettingsService(paths);

        await Assert.ThrowsAsync<JsonException>(() => service.LoadAsync());

        Assert.Equal("{not-json", await File.ReadAllTextAsync(paths.SettingsPath));
    }

    [Fact]
    public async Task Concurrent_saves_leave_one_complete_valid_document()
    {
        using var paths = new TemporaryPaths();
        using var service = new JsonSettingsService(paths);
        ApplicationSettings[] candidates = Enumerable.Range(10, 20)
            .Select(size => new ApplicationSettings { TerminalFontSize = size })
            .ToArray();

        await Task.WhenAll(candidates.Select(candidate => service.SaveAsync(candidate)));

        ApplicationSettings loaded = await service.LoadAsync();
        Assert.Contains(loaded.TerminalFontSize, candidates.Select(item => item.TerminalFontSize));
        Assert.Empty(Directory.EnumerateFiles(paths.ApplicationDirectory, "*.tmp"));
    }
}

internal sealed class TemporaryPaths : IApplicationPathProvider, IDisposable
{
    public TemporaryPaths()
    {
        ApplicationDirectory = Path.Combine(
            Path.GetTempPath(), "HyperTerm.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(ApplicationDirectory);
        DatabasePath = Path.Combine(ApplicationDirectory, "test.db");
        SettingsPath = Path.Combine(ApplicationDirectory, "settings.json");
        LogsDirectory = Path.Combine(ApplicationDirectory, "logs");
    }

    public string ApplicationDirectory { get; }
    public string DatabasePath { get; }
    public string SettingsPath { get; }
    public string LogsDirectory { get; }

    public void Dispose() => Directory.Delete(ApplicationDirectory, recursive: true);
}

internal sealed class EnvironmentScope : IDisposable
{
    private readonly string name;
    private readonly string? previous;

    public EnvironmentScope(string name, string? value)
    {
        this.name = name;
        previous = Environment.GetEnvironmentVariable(name);
        Environment.SetEnvironmentVariable(name, value);
    }

    public void Dispose() => Environment.SetEnvironmentVariable(name, previous);
}
