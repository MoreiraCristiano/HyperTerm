using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using HyperTerm.UI.Services;
using HyperTerm.UI.Views;

namespace HyperTerm.UI;

public sealed partial class App : Application, IDisposable
{
    private readonly Func<MainWindow>? createMainWindow;
    private readonly Func<ApplicationLifecycleCoordinator>? getLifecycle;
    private ApplicationLifecycleCoordinator? lifecycle;

    public App()
    {
    }

    internal App(
        Func<MainWindow> createMainWindow,
        Func<ApplicationLifecycleCoordinator> getLifecycle)
    {
        this.createMainWindow = createMainWindow;
        this.getLifecycle = getLifecycle;
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

            MainWindow mainWindow = createMainWindow();
            lifecycle = getLifecycle();
            mainWindow.Opened += OnMainWindowOpened;
            desktop.Exit += OnDesktopExit;
            desktop.MainWindow = mainWindow;
        }

        base.OnFrameworkInitializationCompleted();
    }

    public void Dispose() => lifecycle?.CancelInitialization();

    private async void OnMainWindowOpened(object? sender, EventArgs eventArgs)
    {
        if (sender is MainWindow window)
        {
            window.Opened -= OnMainWindowOpened;
        }

        await lifecycle!.InitializeAsync();
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
