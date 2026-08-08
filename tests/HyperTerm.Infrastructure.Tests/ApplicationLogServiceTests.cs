using HyperTerm.Infrastructure.Logging;
using HyperTerm.Infrastructure.Storage;
using Microsoft.Extensions.Logging;
using Xunit;

namespace HyperTerm.Infrastructure.Tests;

public sealed class ApplicationLogServiceTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        "HyperTerm.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task WritesTailAndStopsAppendingWhenDisabled()
    {
        var paths = new TestPathProvider(directory);
        using var service = new ApplicationLogService(paths, 1024, 3);
        ILogger logger = service.CreateLogger("Test");
        logger.LogInformation("diagnostic entry");

        string content = await service.ReadTailAsync(
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Contains("diagnostic entry", content);

        service.Configure(false);
        long disabledLength = new FileInfo(Path.Combine(directory, "logs", "hyperterm.log")).Length;
        logger.LogError("must not be written");

        Assert.Equal(
            disabledLength,
            new FileInfo(Path.Combine(directory, "logs", "hyperterm.log")).Length);
    }

    [Fact]
    public void RotatesAndRetainsConfiguredFileCount()
    {
        var paths = new TestPathProvider(directory);
        using var service = new ApplicationLogService(paths, 256, 3);
        ILogger logger = service.CreateLogger("Test");
        for (int index = 0; index < 8; index++)
        {
            logger.LogInformation(
                "Rotation entry {Index}: {Payload}",
                index,
                new string((char)('a' + index), 300));
        }

        string[] logs = Directory.GetFiles(paths.LogsDirectory, "hyperterm*.log");
        Assert.Equal(3, logs.Length);
    }

    [Fact]
    public void DetectsAbandonedRunMarker()
    {
        var paths = new TestPathProvider(directory);
        Directory.CreateDirectory(paths.LogsDirectory);
        File.WriteAllText(Path.Combine(paths.LogsDirectory, "run-999999-1.active"), "stale");

        using var service = new ApplicationLogService(paths, 1024, 3);

        Assert.True(service.PreviousRunCrashed);
    }

    [Fact]
    public void DisabledBootstrapDoesNotCreateLogFilesOrRunMarker()
    {
        var paths = new TestPathProvider(directory);
        File.WriteAllText(paths.SettingsPath, "{\"CaptureLogs\":false}");

        using var service = new ApplicationLogService(paths, 1024, 3);

        Assert.False(service.IsEnabled);
        Assert.False(Directory.Exists(paths.LogsDirectory));
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
