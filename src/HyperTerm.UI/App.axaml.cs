using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using HyperTerm.UI.Services;
using HyperTerm.UI.ViewModels;
using HyperTerm.UI.Views;

namespace HyperTerm.UI;

public sealed partial class App : Application, IDisposable
{
    private readonly MainWindow? mainWindow;
    private readonly MainWindowViewModel? viewModel;
    private readonly ApplicationLifecycleCoordinator? lifecycle;

    public App()
    {
    }

    internal App(
        MainWindow mainWindow,
        MainWindowViewModel viewModel,
        ApplicationLifecycleCoordinator lifecycle)
    {
        this.mainWindow = mainWindow;
        this.viewModel = viewModel;
        this.lifecycle = lifecycle;
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
            if (mainWindow is null || viewModel is null || lifecycle is null)
            {
                throw new InvalidOperationException(
                    "Desktop application services were not configured.");
            }

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
