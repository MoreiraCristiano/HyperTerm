using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using HyperTerm.Core;
using HyperTerm.Core.Abstractions.Persistence;
using HyperTerm.Infrastructure;
using HyperTerm.UI.ViewModels;
using HyperTerm.UI.Views;
using HyperTerm.UI.Services;

namespace HyperTerm.UI;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        using IHost host = CreateHost(args);
        host.StartAsync().GetAwaiter().GetResult();

        IDatabaseInitializer databaseInitializer =
            host.Services.GetRequiredService<IDatabaseInitializer>();
        databaseInitializer.InitializeAsync().GetAwaiter().GetResult();

        MainWindowViewModel mainWindowViewModel =
            host.Services.GetRequiredService<MainWindowViewModel>();
        mainWindowViewModel.InitializeAsync().GetAwaiter().GetResult();

        App.Services = host.Services;
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

        SynchronizationContext.SetSynchronizationContext(null);
        mainWindowViewModel.ShutdownAsync().GetAwaiter().GetResult();
        using var stopTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try
        {
            host.StopAsync(stopTimeout.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
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
                services.AddSingleton<MainWindowViewModel>();
                services.AddSingleton<MainWindow>();
            })
            .Build();
}
