using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;
using HyperTerm.Core.Models;
using HyperTerm.UI.Services;
using HyperTerm.UI.ViewModels;
using HyperTerm.UI.Views;

namespace HyperTerm.UI;

public sealed partial class App : Application, IDisposable
{
    private readonly Func<MainWindow>? createMainWindow;
    private readonly Func<ApplicationLifecycleCoordinator>? getLifecycle;
    private readonly SingleInstanceCoordinator? singleInstance;
    private ApplicationLifecycleCoordinator? lifecycle;
    private IClassicDesktopStyleApplicationLifetime? desktopLifetime;
    private MainWindow? mainWindow;
    private MainWindowViewModel? mainWindowViewModel;
    private TrayIcon? systemTrayIcon;
    private bool explicitShutdownRequested;
    private bool disposed;

    public App()
    {
    }

    internal App(
        Func<MainWindow> createMainWindow,
        Func<ApplicationLifecycleCoordinator> getLifecycle,
        SingleInstanceCoordinator? singleInstance = null)
    {
        this.createMainWindow = createMainWindow;
        this.getLifecycle = getLifecycle;
        this.singleInstance = singleInstance;
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        RequestedThemeVariant = ThemeVariant.Dark;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (createMainWindow is null || getLifecycle is null)
            {
                throw new InvalidOperationException(
                    "Desktop application services were not configured.");
            }

            mainWindow = createMainWindow();
            mainWindowViewModel = mainWindow.DataContext as MainWindowViewModel
                ?? throw new InvalidOperationException("The main window has no view model.");
            desktopLifetime = desktop;
            lifecycle = getLifecycle();
            mainWindow.Opened += OnMainWindowOpened;
            mainWindow.Closing += OnMainWindowClosing;
            mainWindowViewModel.Settings.SettingsSaved += OnSettingsSaved;
            desktop.Exit += OnDesktopExit;
            desktop.MainWindow = mainWindow;
            systemTrayIcon = TrayIcon.GetIcons(this)?.FirstOrDefault();
            singleInstance?.SetActivationHandler(OnExternalActivationRequested);
        }

        base.OnFrameworkInitializationCompleted();
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        lifecycle?.CancelInitialization();
        singleInstance?.ClearActivationHandler();

        if (mainWindow is not null)
        {
            mainWindow.Opened -= OnMainWindowOpened;
            mainWindow.Closing -= OnMainWindowClosing;
        }

        if (mainWindowViewModel is not null)
        {
            mainWindowViewModel.Settings.SettingsSaved -= OnSettingsSaved;
        }

        systemTrayIcon?.Dispose();
        systemTrayIcon = null;
    }

    private async void OnMainWindowOpened(object? sender, EventArgs eventArgs)
    {
        if (sender is MainWindow window)
        {
            window.Opened -= OnMainWindowOpened;
        }

        await lifecycle!.InitializeAsync();
        UpdateSystemTrayVisibility();
    }

    internal static bool ShouldHideToSystemTray(
        bool closeToSystemTray,
        bool explicitShutdownRequested,
        WindowCloseReason closeReason) =>
        closeToSystemTray &&
        !explicitShutdownRequested &&
        closeReason is not WindowCloseReason.ApplicationShutdown and
            not WindowCloseReason.OSShutdown;

    private void OnMainWindowClosing(object? sender, WindowClosingEventArgs eventArgs)
    {
        if (mainWindow is null ||
            mainWindowViewModel is null ||
            !ShouldHideToSystemTray(
                mainWindowViewModel.Settings.Current.CloseToSystemTray,
                explicitShutdownRequested,
                eventArgs.CloseReason))
        {
            return;
        }

        eventArgs.Cancel = true;
        mainWindow.ShowInTaskbar = false;
        mainWindow.Hide();
    }

    private void OnSettingsSaved(ApplicationSettings settings) =>
        UpdateSystemTrayVisibility(settings.CloseToSystemTray);

    private void OnTrayIconClicked(object? sender, EventArgs eventArgs) => OpenFromTray();

    private void OnOpenFromTrayClicked(object? sender, EventArgs eventArgs) => OpenFromTray();

    private void OnExitFromTrayClicked(object? sender, EventArgs eventArgs)
    {
        explicitShutdownRequested = true;
        if (systemTrayIcon is not null)
        {
            systemTrayIcon.IsVisible = false;
        }

        desktopLifetime?.Shutdown();
    }

    private void OpenFromTray()
    {
        if (mainWindow is null)
        {
            return;
        }

        mainWindow.ShowInTaskbar = true;
        if (!mainWindow.IsVisible)
        {
            mainWindow.Show();
        }

        if (mainWindow.WindowState == WindowState.Minimized)
        {
            mainWindow.WindowState = WindowState.Normal;
        }

        mainWindow.Activate();
        WindowsApplicationActivation.TryBringToForeground(mainWindow);
    }

    private void OnExternalActivationRequested() =>
        Dispatcher.UIThread.Post(OpenFromTray, DispatcherPriority.Send);

    private void UpdateSystemTrayVisibility(bool? closeToSystemTray = null)
    {
        if (systemTrayIcon is not null)
        {
            systemTrayIcon.IsVisible = closeToSystemTray ??
                mainWindowViewModel?.Settings.Current.CloseToSystemTray == true;
        }
    }

    private void OnDesktopExit(object? sender, ControlledApplicationLifetimeExitEventArgs eventArgs)
    {
        if (sender is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Exit -= OnDesktopExit;
        }

        Dispose();
    }
}
