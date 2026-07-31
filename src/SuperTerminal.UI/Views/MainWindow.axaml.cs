using Avalonia;
using Avalonia.Controls;
using SuperTerminal.Core.Models;
using SuperTerminal.UI.ViewModels;

namespace SuperTerminal.UI.Views;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    public MainWindow(MainWindowViewModel viewModel)
        : this()
    {
        DataContext = viewModel;
        Opened += (_, _) => RestoreWindowState(viewModel.WindowSettings);
        Closing += (_, _) => viewModel.CaptureWindowState(
            Bounds.Width,
            Bounds.Height,
            Position.X,
            Position.Y);
    }

    private void RestoreWindowState(WindowSettings settings)
    {
        Width = Math.Max(MinWidth, settings.Width);
        Height = Math.Max(MinHeight, settings.Height);

        if (settings.X is not int x || settings.Y is not int y)
        {
            return;
        }

        var savedPosition = new PixelPoint(x, y);
        if (Screens.All.Any(screen => screen.WorkingArea.Contains(savedPosition)))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = savedPosition;
        }
    }
}
