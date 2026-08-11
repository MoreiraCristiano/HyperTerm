using Avalonia.Controls;
using Avalonia.Interactivity;

namespace HyperTerm.UI.Views.Dialogs;

public sealed partial class SettingsDialog : UserControl
{
    public SettingsDialog()
    {
        InitializeComponent();
    }

    private void OnGeneralClick(object? sender, RoutedEventArgs eventArgs) =>
        GeneralGroup.BringIntoView();

    private void OnTerminalClick(object? sender, RoutedEventArgs eventArgs) =>
        TerminalGroup.BringIntoView();

    private void OnDataClick(object? sender, RoutedEventArgs eventArgs) =>
        DataGroup.BringIntoView();

    private void OnLogsClick(object? sender, RoutedEventArgs eventArgs) =>
        LogsGroup.BringIntoView();
}
