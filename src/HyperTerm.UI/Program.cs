using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using HyperTerm.Core;
using HyperTerm.Infrastructure;
using HyperTerm.UI.ViewModels;
using HyperTerm.UI.Views;
using HyperTerm.UI.Services;
using HyperTerm.Core.Abstractions.Logging;
using Microsoft.Extensions.Logging;

namespace HyperTerm.UI;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var singleInstance = new SingleInstanceCoordinator(
            SingleInstanceCoordinator.CreateIdentity());

        try
        {
            if (!singleInstance.IsPrimary)
            {
                bool activationRequested = singleInstance.RequestActivationAsync(
                        TimeSpan.FromSeconds(3))
                    .GetAwaiter()
                    .GetResult();
                if (!activationRequested)
                {
                    WindowsApplicationActivation.ShowActivationFailure();
                }

                return;
            }

            singleInstance.StartListening();
            using IHost host = CreateHost(args);
            host.StartAsync().GetAwaiter().GetResult();
            using ApplicationExceptionMonitor exceptionMonitor =
                host.Services.GetRequiredService<ApplicationExceptionMonitor>();
            exceptionMonitor.Start();
            ILogger logger = host.Services.GetRequiredService<ILoggerFactory>()
                .CreateLogger("HyperTerm");
            IApplicationLogService logService =
                host.Services.GetRequiredService<IApplicationLogService>();

            try
            {
                logger.LogInformation("Avalonia desktop lifetime starting.");
                BuildAvaloniaApp(host.Services, singleInstance)
                    .StartWithClassicDesktopLifetime(args);

                SynchronizationContext.SetSynchronizationContext(null);
                ApplicationLifecycleCoordinator lifecycle =
                    host.Services.GetRequiredService<ApplicationLifecycleCoordinator>();
                lifecycle.ShutdownAsync().GetAwaiter().GetResult();
                using var stopTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                try
                {
                    host.StopAsync(stopTimeout.Token).GetAwaiter().GetResult();
                }
                catch (OperationCanceledException)
                {
                    logger.LogWarning("Host shutdown exceeded the two-second timeout.");
                }

                logService.CompleteRun();
            }
            catch (Exception exception)
            {
                logger.LogCritical(exception, "HyperTerm terminated unexpectedly.");
                throw;
            }
        }
        finally
        {
            singleInstance.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    private static AppBuilder BuildAvaloniaApp(
        IServiceProvider services,
        SingleInstanceCoordinator singleInstance) =>
        AppBuilder.Configure(() => new App(
                () => services.GetRequiredService<MainWindow>(),
                () => services.GetRequiredService<ApplicationLifecycleCoordinator>(),
                singleInstance))
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    private static IHost CreateHost(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureServices(static services =>
            {
                services.AddCore();
                services.AddInfrastructure();
                services.AddSingleton<IThemeService, AvaloniaThemeService>();
                services.AddSingleton<IExecutableFilePicker, ExecutableFilePicker>();
                services.AddSingleton<ISessionArchiveFilePicker, SessionArchiveFilePicker>();
                services.AddSingleton<ISystemFontService, AvaloniaSystemFontService>();
                services.AddSingleton<ILogInteractionService, LogInteractionService>();
                services.AddSingleton<ApplicationExceptionMonitor>();
                services.AddSingleton<ApplicationLifecycleCoordinator>();
                services.AddSingleton<SessionExplorerViewModel>();
                services.AddSingleton<TerminalWorkspaceViewModel>();
                services.AddSingleton<SettingsViewModel>();
                services.AddSingleton<SessionEditorViewModel>();
                services.AddSingleton<SessionManagerViewModel>();
                services.AddSingleton<FolderEditorViewModel>();
                services.AddSingleton<MainWindowViewModel>();
                services.AddSingleton<MainWindow>();
            })
            .Build();
}
