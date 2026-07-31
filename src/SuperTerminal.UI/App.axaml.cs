using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Microsoft.Extensions.DependencyInjection;
using SuperTerminal.UI.Views;
using SuperTerminal.UI.Services;
using SuperTerminal.UI.ViewModels;

namespace SuperTerminal.UI;

public sealed partial class App : Application
{
    internal static IServiceProvider Services { private get; set; } = null!;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        RequestedThemeVariant = ThemeVariant.Dark;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            MainWindowViewModel viewModel = Services.GetRequiredService<MainWindowViewModel>();
            Services.GetRequiredService<IThemeService>().Apply(viewModel.SettingsTheme);
            desktop.MainWindow = Services.GetRequiredService<MainWindow>();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
