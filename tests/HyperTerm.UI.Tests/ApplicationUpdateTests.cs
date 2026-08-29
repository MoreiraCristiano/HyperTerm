using System.Text.Json;
using HyperTerm.Core.Abstractions.Services;
using HyperTerm.Core.Models;
using HyperTerm.UI.Services;
using HyperTerm.UI.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace HyperTerm.UI.Tests;

public sealed class ApplicationUpdateViewModelTests
{
    [Fact]
    public void Dependency_injection_resolves_update_view_model()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IApplicationUpdateService, FakeApplicationUpdateService>();
        services.AddSingleton<IApplicationUpdateLauncher, FakeApplicationUpdateLauncher>();
        services.AddSingleton<ApplicationUpdateViewModel>();
        using ServiceProvider provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        Assert.NotNull(provider.GetRequiredService<ApplicationUpdateViewModel>());
    }

    [Fact]
    public async Task Check_download_and_install_follow_explicit_state_transitions()
    {
        var update = new ApplicationUpdateInfo(
            new Version(2, 0, 0),
            new Uri("https://github.test/release"),
            new Uri("https://download.test/package"),
            new Uri("https://download.test/checksum"),
            "HyperTerm-2.0.0-win-x64.zip");
        var service = new FakeApplicationUpdateService { AvailableUpdate = update };
        var launcher = new FakeApplicationUpdateLauncher();
        using var viewModel = new ApplicationUpdateViewModel(
            service,
            launcher,
            NullLogger<ApplicationUpdateViewModel>.Instance,
            new Version(1, 0, 0));

        await viewModel.CheckForUpdatesCommand.ExecuteAsync(null);

        Assert.Equal(ApplicationUpdateState.Available, viewModel.State);
        Assert.True(viewModel.IsBannerVisible);
        Assert.Equal("2.0.0", viewModel.AvailableVersionText);

        await viewModel.DownloadUpdateCommand.ExecuteAsync(null);

        Assert.Equal(ApplicationUpdateState.Ready, viewModel.State);
        Assert.True(viewModel.IsReadyToInstall);
        bool restartRequested = false;
        viewModel.RestartRequested += (_, _) => restartRequested = true;

        await viewModel.InstallUpdateCommand.ExecuteAsync(null);

        Assert.True(launcher.WasLaunched);
        Assert.True(restartRequested);
        Assert.Equal(ApplicationUpdateState.Installing, viewModel.State);
    }

    [Fact]
    public async Task Automatic_check_failure_is_logged_without_user_error()
    {
        var service = new FakeApplicationUpdateService
        {
            CheckError = new HttpRequestException("offline"),
        };
        using var viewModel = new ApplicationUpdateViewModel(
            service,
            new FakeApplicationUpdateLauncher(),
            NullLogger<ApplicationUpdateViewModel>.Instance,
            new Version(1, 0, 0));

        viewModel.StartAutomaticCheck();
        await viewModel.AutomaticCheckCompletion;

        Assert.Equal(ApplicationUpdateState.Failed, viewModel.State);
        Assert.False(viewModel.HasError);
        Assert.False(viewModel.IsBannerVisible);
    }

    [Fact]
    public async Task Canceled_uac_keeps_ready_update_without_restart()
    {
        var update = new ApplicationUpdateInfo(
            new Version(2, 0, 0),
            new Uri("https://github.test/release"),
            new Uri("https://download.test/package"),
            new Uri("https://download.test/checksum"),
            "HyperTerm-2.0.0-win-x64.zip");
        var service = new FakeApplicationUpdateService { AvailableUpdate = update };
        var launcher = new FakeApplicationUpdateLauncher { LaunchResult = false };
        using var viewModel = new ApplicationUpdateViewModel(
            service,
            launcher,
            NullLogger<ApplicationUpdateViewModel>.Instance,
            new Version(1, 0, 0));
        await viewModel.CheckForUpdatesCommand.ExecuteAsync(null);
        await viewModel.DownloadUpdateCommand.ExecuteAsync(null);
        bool restartRequested = false;
        viewModel.RestartRequested += (_, _) => restartRequested = true;

        await viewModel.InstallUpdateCommand.ExecuteAsync(null);

        Assert.Equal(ApplicationUpdateState.Ready, viewModel.State);
        Assert.False(restartRequested);
        Assert.Contains("canceled", viewModel.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FakeApplicationUpdateService : IApplicationUpdateService
    {
        public ApplicationUpdateInfo? AvailableUpdate { get; init; }
        public Exception? CheckError { get; init; }

        public Task<ApplicationUpdateInfo?> CheckAsync(
            Version currentVersion,
            CancellationToken cancellationToken = default) =>
            CheckError is null
                ? Task.FromResult(AvailableUpdate)
                : Task.FromException<ApplicationUpdateInfo?>(CheckError);

        public Task<PreparedApplicationUpdate> PrepareAsync(
            ApplicationUpdateInfo update,
            IProgress<ApplicationUpdateProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            progress?.Report(new ApplicationUpdateProgress(10, 10));
            return Task.FromResult(new PreparedApplicationUpdate(
                update.Version,
                @"C:\staging",
                @"C:\staging\HyperTerm.exe"));
        }
    }

    private sealed class FakeApplicationUpdateLauncher : IApplicationUpdateLauncher
    {
        public bool LaunchResult { get; init; } = true;
        public bool WasLaunched { get; private set; }

        public Task<bool> LaunchAsync(
            PreparedApplicationUpdate update,
            CancellationToken cancellationToken = default)
        {
            WasLaunched = true;
            return Task.FromResult(LaunchResult);
        }
    }
}

public sealed class ApplicationUpdateApplierTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "HyperTerm.ApplierTests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Apply_replaces_managed_files_removes_obsolete_and_preserves_unknown()
    {
        string source = Path.Combine(root, "source");
        string target = Path.Combine(root, "target");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(target);
        WriteFile(source, "HyperTerm.exe", "new executable");
        WriteFile(source, "new.dll", "new library");
        WriteManifest(source, ["HyperTerm.exe", "new.dll", "HyperTerm.manifest.json"]);
        WriteFile(target, "HyperTerm.exe", "old executable");
        WriteFile(target, "obsolete.dll", "obsolete");
        WriteFile(target, "user-notes.txt", "preserve");
        WriteManifest(target, ["HyperTerm.exe", "obsolete.dll", "HyperTerm.manifest.json"]);

        await ApplicationUpdateApplier.ApplyAsync(source, target);

        Assert.Equal("new executable", ReadFile(target, "HyperTerm.exe"));
        Assert.Equal("new library", ReadFile(target, "new.dll"));
        Assert.False(File.Exists(Path.Combine(target, "obsolete.dll")));
        Assert.Equal("preserve", ReadFile(target, "user-notes.txt"));
    }

    [Fact]
    public async Task Apply_rolls_back_files_when_source_is_incomplete()
    {
        string source = Path.Combine(root, "source");
        string target = Path.Combine(root, "target");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(target);
        WriteFile(source, "HyperTerm.exe", "new executable");
        WriteManifest(source, ["HyperTerm.exe", "missing.dll", "HyperTerm.manifest.json"]);
        WriteFile(target, "HyperTerm.exe", "old executable");
        WriteManifest(target, ["HyperTerm.exe", "HyperTerm.manifest.json"]);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            ApplicationUpdateApplier.ApplyAsync(source, target));

        Assert.Equal("old executable", ReadFile(target, "HyperTerm.exe"));
    }

    [Fact]
    public void Arguments_require_target_and_wait_process()
    {
        string target = Path.Combine(root, "target");
        Directory.CreateDirectory(target);
        WriteFile(target, "HyperTerm.exe", "old executable");

        ApplicationUpdateRequest request = ApplicationUpdateApplier.ParseArguments(
            ["--apply-update", "--target", target, "--wait-pid", "42"]);

        Assert.Equal(Path.GetFullPath(target), request.TargetDirectory);
        Assert.Equal(42, request.ProcessId);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void WriteManifest(string directory, string[] files) =>
        WriteFile(directory, "HyperTerm.manifest.json", JsonSerializer.Serialize(new
        {
            schemaVersion = 2,
            product = "HyperTerm",
            version = "1.0.0",
            runtime = "win-x64",
            files,
        }));

    private static void WriteFile(string directory, string relativePath, string content)
    {
        string path = Path.Combine(directory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static string ReadFile(string directory, string relativePath) =>
        File.ReadAllText(Path.Combine(directory, relativePath));
}
