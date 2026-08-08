using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HyperTerm.UI.ViewModels;

namespace HyperTerm.UI.Views.Dialogs;

public sealed partial class PsmuxSessionsDialog : UserControl
{
    public PsmuxSessionsDialog()
    {
        InitializeComponent();
    }

    internal void FocusContent(MainWindowViewModel viewModel)
    {
        Dispatcher.UIThread.Post(
            () =>
            {
                if (!viewModel.Workspace.IsPsmuxSessionsOpen)
                {
                    return;
                }

                if (viewModel.Workspace.PsmuxSessions.Count > 0)
                {
                    viewModel.Workspace.SelectedPsmuxSession ??=
                        viewModel.Workspace.PsmuxSessions[0];
                    ActivePsmuxSessionsList.Focus();
                }
                else
                {
                    RefreshPsmuxSessionsButton.Focus();
                }
            },
            DispatcherPriority.Loaded);
    }

    private void OnPsmuxSessionsDialogKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        if (eventArgs.Key == Key.Escape)
        {
            viewModel.Workspace.ClosePsmuxSessionsCommand.Execute(null);
            eventArgs.Handled = true;
        }
        else if (eventArgs.Key == Key.Enter &&
                 eventArgs.Source is not Button &&
                 viewModel.Workspace.AttachSelectedPsmuxSessionCommand.CanExecute(null))
        {
            viewModel.Workspace.AttachSelectedPsmuxSessionCommand.Execute(null);
            eventArgs.Handled = true;
        }
    }

    private void OnRequestKillPsmuxSession(object? sender, RoutedEventArgs eventArgs)
    {
        if (sender is not Button { DataContext: PsmuxSessionItemViewModel session } ||
            DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        viewModel.Workspace.RequestKillPsmuxSessionCommand.Execute(session);
        eventArgs.Handled = true;
    }

    private void OnActivePsmuxSessionDoubleTapped(object? sender, TappedEventArgs eventArgs)
    {
        if (eventArgs.Source is not Visual source ||
            source.FindAncestorOfType<ListBoxItem>()?.DataContext is not
                PsmuxSessionItemViewModel session ||
            DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        viewModel.Workspace.SelectedPsmuxSession = session;
        if (viewModel.Workspace.AttachSelectedPsmuxSessionCommand.CanExecute(null))
        {
            viewModel.Workspace.AttachSelectedPsmuxSessionCommand.Execute(null);
            eventArgs.Handled = true;
        }
    }
}

public sealed partial class PsmuxKillDialog : UserControl
{
    public PsmuxKillDialog()
    {
        InitializeComponent();
    }

    internal void FocusCancel() =>
        Dispatcher.UIThread.Post(
            () => CancelPsmuxKillButton.Focus(),
            DispatcherPriority.Loaded);

    private void OnPsmuxKillConfirmationKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        if (eventArgs.Key == Key.Escape &&
            viewModel.Workspace.CancelKillPsmuxSessionCommand.CanExecute(null))
        {
            viewModel.Workspace.CancelKillPsmuxSessionCommand.Execute(null);
            eventArgs.Handled = true;
        }
        else if (eventArgs.Key == Key.Enter && eventArgs.KeyModifiers == KeyModifiers.None &&
                 viewModel.Workspace.ConfirmKillPsmuxSessionCommand.CanExecute(null))
        {
            viewModel.Workspace.ConfirmKillPsmuxSessionCommand.Execute(null);
            eventArgs.Handled = true;
        }
    }
}

public sealed partial class PsmuxCreateDialog : UserControl
{
    public PsmuxCreateDialog()
    {
        InitializeComponent();
    }

    internal TextBox NameEditor => PsmuxSessionNameEditor;

    private void OnPsmuxDialogKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        if (eventArgs.Key == Key.Escape)
        {
            viewModel.Workspace.CancelPsmuxCreateCommand.Execute(null);
            eventArgs.Handled = true;
        }
        else if (eventArgs.Key == Key.Enter && eventArgs.KeyModifiers == KeyModifiers.None &&
                 viewModel.Workspace.ConfirmPsmuxCreateCommand.CanExecute(null))
        {
            viewModel.Workspace.ConfirmPsmuxCreateCommand.Execute(null);
            eventArgs.Handled = true;
        }
    }
}
