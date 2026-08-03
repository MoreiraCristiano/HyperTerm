using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using HyperTerm.Core.Abstractions.Persistence;
using HyperTerm.UI.Views;
using HyperTerm.UI.ViewModels;

namespace HyperTerm.UI;

public sealed partial class App : Application
{
    internal static IServiceProvider Services { private get; set; } = null!;
    private CancellationTokenSource? startupCancellation;
    private Task? startupTask;
    private MainWindowViewModel? mainWindowViewModel;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        RequestedThemeVariant = ThemeVariant.Dark;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            mainWindowViewModel = Services.GetRequiredService<MainWindowViewModel>();
            MainWindow mainWindow = Services.GetRequiredService<MainWindow>();
            mainWindow.Opened += OnMainWindowOpened;
            desktop.Exit += (_, _) => startupCancellation?.Cancel();
            desktop.MainWindow = mainWindow;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void OnMainWindowOpened(object? sender, EventArgs eventArgs)
    {
        if (startupTask is not null || mainWindowViewModel is null)
        {
            return;
        }

        if (sender is Window window)
        {
            window.Opened -= OnMainWindowOpened;
        }

        startupCancellation = new CancellationTokenSource();
        Dispatcher.UIThread.Post(
            () => startupTask = InitializeApplicationAsync(
                mainWindowViewModel,
                startupCancellation.Token),
            DispatcherPriority.Background);
    }

    private static async Task InitializeApplicationAsync(
        MainWindowViewModel viewModel,
        CancellationToken cancellationToken)
    {
        try
        {
            ILogger logger = Services.GetRequiredService<ILoggerFactory>()
                .CreateLogger("HyperTerm.Startup");
            logger.LogInformation("Application initialization started.");
            Task databaseInitialization = Task.Run(
                () => Services
                    .GetRequiredService<IDatabaseInitializer>()
                    .InitializeAsync(cancellationToken),
                cancellationToken);
            await viewModel.InitializeSettingsAsync(cancellationToken);
            await databaseInitialization;
            await viewModel.InitializeWorkspaceAsync(cancellationToken);
            viewModel.CompleteInitialization();
            logger.LogInformation("Application initialization completed.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Services.GetRequiredService<ILoggerFactory>()
                .CreateLogger("HyperTerm.Startup")
                .LogError(exception, "Application initialization failed.");
            viewModel.ReportStartupFailure(exception);
        }
    }
}
