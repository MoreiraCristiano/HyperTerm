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
        using IHost host = CreateHost(args);
        host.StartAsync().GetAwaiter().GetResult();
        using ApplicationExceptionMonitor exceptionMonitor =
            host.Services.GetRequiredService<ApplicationExceptionMonitor>();
        exceptionMonitor.Start();
        ILogger logger = host.Services.GetRequiredService<ILoggerFactory>()
            .CreateLogger("HyperTerm");
        IApplicationLogService logService =
            host.Services.GetRequiredService<IApplicationLogService>();

        App.Services = host.Services;
        try
        {
            logger.LogInformation("Avalonia desktop lifetime starting.");
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

            SynchronizationContext.SetSynchronizationContext(null);
            MainWindowViewModel mainWindowViewModel =
                host.Services.GetRequiredService<MainWindowViewModel>();
            mainWindowViewModel.ShutdownAsync().GetAwaiter().GetResult();
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

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
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
                services.AddSingleton<SessionExplorerViewModel>();
                services.AddSingleton<TerminalWorkspaceViewModel>();
                services.AddSingleton<SettingsViewModel>();
                services.AddSingleton<SessionEditorViewModel>();
                services.AddSingleton<FolderEditorViewModel>();
                services.AddSingleton<MainWindowViewModel>();
                services.AddSingleton<MainWindow>();
            })
            .Build();
}
