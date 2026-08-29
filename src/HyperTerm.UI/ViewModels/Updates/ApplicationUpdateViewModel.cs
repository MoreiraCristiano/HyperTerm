using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HyperTerm.Core.Abstractions.Services;
using HyperTerm.Core.Models;
using HyperTerm.UI.Services;
using Microsoft.Extensions.Logging;

namespace HyperTerm.UI.ViewModels;

public enum ApplicationUpdateState
{
    Idle,
    Checking,
    Available,
    Downloading,
    Ready,
    Installing,
    UpToDate,
    Failed,
}

public sealed partial class ApplicationUpdateViewModel : ViewModelBase, IDisposable
{
    private readonly IApplicationUpdateService updateService;
    private readonly IApplicationUpdateLauncher updateLauncher;
    private readonly ILogger<ApplicationUpdateViewModel> logger;
    private readonly CancellationTokenSource lifetimeCancellation = new();
    private Task? automaticCheckTask;
    private ApplicationUpdateInfo? availableUpdate;
    private PreparedApplicationUpdate? preparedUpdate;
    private bool bannerDismissed;
    private bool disposed;

    public ApplicationUpdateViewModel(
        IApplicationUpdateService updateService,
        IApplicationUpdateLauncher updateLauncher,
        ILogger<ApplicationUpdateViewModel> logger)
    {
        this.updateService = updateService;
        this.updateLauncher = updateLauncher;
        this.logger = logger;
        Version? assemblyVersion = Assembly.GetEntryAssembly()?.GetName().Version ??
            typeof(ApplicationUpdateViewModel).Assembly.GetName().Version;
        CurrentVersion = new Version(
            assemblyVersion?.Major ?? 1,
            assemblyVersion?.Minor ?? 0,
            Math.Max(0, assemblyVersion?.Build ?? 0));
    }

    internal ApplicationUpdateViewModel(
        IApplicationUpdateService updateService,
        IApplicationUpdateLauncher updateLauncher,
        ILogger<ApplicationUpdateViewModel> logger,
        Version currentVersion)
        : this(updateService, updateLauncher, logger)
    {
        CurrentVersion = currentVersion;
    }

    public event EventHandler? RestartRequested;
    public event EventHandler? OpenDetailsRequested;

    public Version CurrentVersion { get; private set; }
    public string CurrentVersionText => CurrentVersion.ToString();
    public string? AvailableVersionText => availableUpdate?.Version.ToString();
    public bool IsBusy => State is ApplicationUpdateState.Checking or
        ApplicationUpdateState.Downloading or ApplicationUpdateState.Installing;
    public bool IsUpdateAvailable => availableUpdate is not null;
    public bool IsReadyToInstall => preparedUpdate is not null &&
        State == ApplicationUpdateState.Ready;
    public bool IsBannerVisible => IsUpdateAvailable && !bannerDismissed;
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public bool CanDownload => availableUpdate is not null && !IsBusy && preparedUpdate is null;
    public string BannerText => availableUpdate is null
        ? string.Empty
        : $"HyperTerm {availableUpdate.Version} is available.";
    internal Task AutomaticCheckCompletion => automaticCheckTask ?? Task.CompletedTask;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    [NotifyPropertyChangedFor(nameof(IsReadyToInstall))]
    [NotifyPropertyChangedFor(nameof(CanDownload))]
    private ApplicationUpdateState state;

    [ObservableProperty]
    private string statusText = "Updates have not been checked yet.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? errorMessage;

    [ObservableProperty]
    private int downloadPercentage;

    public void StartAutomaticCheck()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        automaticCheckTask ??= CheckCoreAsync(
            automatic: true,
            lifetimeCancellation.Token);
    }

    public async Task ShutdownAsync()
    {
        lifetimeCancellation.Cancel();
        Task[] pendingTasks =
        [
            automaticCheckTask ?? Task.CompletedTask,
            CheckForUpdatesCommand.ExecutionTask ?? Task.CompletedTask,
            DownloadUpdateCommand.ExecutionTask ?? Task.CompletedTask,
            InstallUpdateCommand.ExecutionTask ?? Task.CompletedTask,
        ];
        try
        {
            await Task.WhenAll(pendingTasks).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private Task CheckForUpdatesAsync() =>
        CheckCoreAsync(automatic: false, lifetimeCancellation.Token);

    [RelayCommand(AllowConcurrentExecutions = false, CanExecute = nameof(CanDownloadUpdate))]
    private async Task DownloadUpdateAsync()
    {
        if (availableUpdate is null)
        {
            return;
        }

        State = ApplicationUpdateState.Downloading;
        ErrorMessage = null;
        StatusText = $"Downloading HyperTerm {availableUpdate.Version}...";
        DownloadPercentage = 0;
        var progress = new Progress<ApplicationUpdateProgress>(value =>
        {
            if (State != ApplicationUpdateState.Downloading)
            {
                return;
            }

            DownloadPercentage = value.Percentage;
            StatusText = value.TotalBytes is > 0
                ? $"Downloading HyperTerm {availableUpdate.Version}: {value.Percentage}%"
                : $"Downloading HyperTerm {availableUpdate.Version}...";
        });

        try
        {
            preparedUpdate = await updateService.PrepareAsync(
                availableUpdate,
                progress,
                lifetimeCancellation.Token);
            DownloadPercentage = 100;
            State = ApplicationUpdateState.Ready;
            StatusText = $"HyperTerm {availableUpdate.Version} is ready to install.";
        }
        catch (OperationCanceledException) when (lifetimeCancellation.IsCancellationRequested)
        {
            State = ApplicationUpdateState.Available;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to download application update.");
            ErrorMessage = $"Update download failed: {exception.Message}";
            State = ApplicationUpdateState.Failed;
            StatusText = "The update could not be prepared.";
        }
        finally
        {
            NotifyStateProperties();
        }
    }

    private bool CanDownloadUpdate() => CanDownload;

    [RelayCommand(CanExecute = nameof(CanInstallUpdate))]
    private async Task InstallUpdateAsync()
    {
        if (preparedUpdate is null)
        {
            return;
        }

        State = ApplicationUpdateState.Installing;
        ErrorMessage = null;
        StatusText = "Waiting for permission to install the update...";
        try
        {
            bool launched = await updateLauncher.LaunchAsync(
                preparedUpdate,
                lifetimeCancellation.Token);
            if (!launched)
            {
                State = ApplicationUpdateState.Ready;
                StatusText = "Update installation was canceled.";
                return;
            }

            StatusText = "Closing HyperTerm to install the update...";
            RestartRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
                System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            logger.LogError(exception, "Failed to start application update installer.");
            ErrorMessage = $"Update installation failed: {exception.Message}";
            State = ApplicationUpdateState.Ready;
            StatusText = "The update installer could not be started.";
        }
        finally
        {
            NotifyStateProperties();
        }
    }

    private bool CanInstallUpdate() => IsReadyToInstall;

    [RelayCommand]
    private void DismissBanner()
    {
        bannerDismissed = true;
        OnPropertyChanged(nameof(IsBannerVisible));
    }

    [RelayCommand]
    private void OpenDetails() => OpenDetailsRequested?.Invoke(this, EventArgs.Empty);

    private async Task CheckCoreAsync(bool automatic, CancellationToken cancellationToken)
    {
        if (IsBusy)
        {
            return;
        }

        State = ApplicationUpdateState.Checking;
        ErrorMessage = null;
        StatusText = "Checking for updates...";
        try
        {
            ApplicationUpdateInfo? update = await updateService.CheckAsync(
                CurrentVersion,
                cancellationToken);
            availableUpdate = update;
            preparedUpdate = null;
            if (update is null)
            {
                State = ApplicationUpdateState.UpToDate;
                StatusText = $"HyperTerm {CurrentVersion} is up to date.";
            }
            else
            {
                bannerDismissed = false;
                State = ApplicationUpdateState.Available;
                StatusText = $"HyperTerm {update.Version} is available.";
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            State = ApplicationUpdateState.Idle;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to check for application updates.");
            State = ApplicationUpdateState.Failed;
            StatusText = "HyperTerm could not check for updates.";
            if (!automatic)
            {
                ErrorMessage = $"Update check failed: {exception.Message}";
            }
        }
        finally
        {
            NotifyStateProperties();
        }
    }

    private void NotifyStateProperties()
    {
        OnPropertyChanged(nameof(AvailableVersionText));
        OnPropertyChanged(nameof(IsUpdateAvailable));
        OnPropertyChanged(nameof(IsBannerVisible));
        OnPropertyChanged(nameof(BannerText));
        OnPropertyChanged(nameof(IsReadyToInstall));
        OnPropertyChanged(nameof(CanDownload));
        DownloadUpdateCommand.NotifyCanExecuteChanged();
        InstallUpdateCommand.NotifyCanExecuteChanged();
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        lifetimeCancellation.Cancel();
        lifetimeCancellation.Dispose();
    }
}
