using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SuperTerminal.Core;
using SuperTerminal.Core.Abstractions.Persistence;
using SuperTerminal.Infrastructure;
using SuperTerminal.UI.ViewModels;
using SuperTerminal.UI.Views;
using SuperTerminal.UI.Services;

namespace SuperTerminal.UI;

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

        mainWindowViewModel.ShutdownAsync().GetAwaiter().GetResult();
        host.StopAsync().GetAwaiter().GetResult();
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
                services.AddSingleton<MainWindowViewModel>();
                services.AddSingleton<MainWindow>();
            })
            .Build();
}
