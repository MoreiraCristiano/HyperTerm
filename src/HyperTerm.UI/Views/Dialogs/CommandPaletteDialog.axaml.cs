using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using HyperTerm.UI.ViewModels;

namespace HyperTerm.UI.Views.Dialogs;

public sealed partial class CommandPaletteDialog : UserControl
{
    private int focusRequestGeneration;

    public CommandPaletteDialog()
    {
        InitializeComponent();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != IsVisibleProperty)
        {
            return;
        }

        if (change.GetNewValue<bool>())
        {
            ScheduleQueryFocus();
        }
        else
        {
            focusRequestGeneration++;
        }
    }

    private void ScheduleQueryFocus()
    {
        int generation = ++focusRequestGeneration;
        Dispatcher.UIThread.Post(
            () =>
            {
                FocusQueryIfCurrent(generation);
                Dispatcher.UIThread.Post(
                    () => FocusQueryIfCurrent(generation),
                    DispatcherPriority.Background);
            },
            DispatcherPriority.Render);
    }

    internal void FocusQueryAfterNativeFocusRelease() => ScheduleQueryFocus();

    private void FocusQueryIfCurrent(int generation)
    {
        if (generation != focusRequestGeneration || !IsVisible)
        {
            return;
        }

        QueryEditor.Focus(NavigationMethod.Tab, KeyModifiers.None);
        QueryEditor.SelectAll();
    }

    private void OnQueryKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        switch (eventArgs.Key)
        {
            case Key.Down:
                viewModel.MoveCommandPaletteSelection(1);
                eventArgs.Handled = true;
                break;
            case Key.Up:
                viewModel.MoveCommandPaletteSelection(-1);
                eventArgs.Handled = true;
                break;
            case Key.Enter:
                _ = viewModel.ExecuteSelectedCommandPaletteItemCommand.ExecuteAsync(null);
                eventArgs.Handled = true;
                break;
            case Key.Escape:
                viewModel.CloseCommandPaletteCommand.Execute(null);
                eventArgs.Handled = true;
                break;
        }
    }

    private void OnResultDoubleTapped(object? sender, TappedEventArgs eventArgs)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            _ = viewModel.ExecuteSelectedCommandPaletteItemCommand.ExecuteAsync(null);
            eventArgs.Handled = true;
        }
    }
}
