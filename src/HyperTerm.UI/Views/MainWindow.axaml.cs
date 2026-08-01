using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HyperTerm.Core.Models;
using HyperTerm.UI.ViewModels;

namespace HyperTerm.UI.Views;

public sealed partial class MainWindow : Window
{
    private static readonly DataFormat<string> SessionDragFormat =
        DataFormat.CreateInProcessFormat<string>("HyperTerm.SessionId");

    private PointerPressedEventArgs? sessionDragStartEvent;
    private Point sessionDragStartPoint;
    private SessionTreeNodeViewModel? draggedSessionNode;
    private TreeViewItem? currentDropTarget;
    private readonly double sidebarMinimumWidth;
    private readonly double sidebarMaximumWidth;
    private readonly GridLength sidebarSplitterWidth;
    private double sidebarWidth;

    private ColumnDefinition SidebarColumn => WorkspaceGrid.ColumnDefinitions[0];

    private ColumnDefinition SidebarSplitterColumn => WorkspaceGrid.ColumnDefinitions[1];

    public MainWindow()
    {
        InitializeComponent();
        UpdateMaximizeRestoreIcon();
        TitleBarRoot.AddHandler(
            InputElement.PointerPressedEvent,
            OnTitleBarPointerPressed,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        TerminalTabs.AddHandler(
            InputElement.PointerPressedEvent,
            OnTerminalTabPointerPressed,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        SessionsTree.AddHandler(
            InputElement.PointerPressedEvent,
            OnSessionTreePointerPressed,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        SessionsTree.AddHandler(
            InputElement.PointerMovedEvent,
            OnSessionTreePointerMoved,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        SessionsTree.AddHandler(
            InputElement.PointerReleasedEvent,
            OnSessionTreePointerReleased,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        DragDrop.SetAllowDrop(SessionsTree, true);
        DragDrop.AddDragOverHandler(SessionsTree, OnSessionTreeDragOver);
        DragDrop.AddDragLeaveHandler(SessionsTree, OnSessionTreeDragLeave);
        DragDrop.AddDropHandler(SessionsTree, OnSessionTreeDrop);
        sidebarMinimumWidth = SidebarColumn.MinWidth;
        sidebarMaximumWidth = SidebarColumn.MaxWidth;
        sidebarSplitterWidth = SidebarSplitterColumn.Width;
        sidebarWidth = SidebarColumn.Width.Value;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == WindowStateProperty)
        {
            UpdateMaximizeRestoreIcon();
        }
    }

    private void OnMinimizeButtonClick(object? sender, RoutedEventArgs eventArgs) =>
        WindowState = WindowState.Minimized;

    private void OnMaximizeRestoreButtonClick(object? sender, RoutedEventArgs eventArgs) =>
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    private void OnCloseButtonClick(object? sender, RoutedEventArgs eventArgs) => Close();

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs eventArgs)
    {
        PointerPoint point = eventArgs.GetCurrentPoint(TitleBarRoot);
        if (!point.Properties.IsLeftButtonPressed ||
            eventArgs.Source is not Visual source ||
            source.FindAncestorOfType<Button>() is not null ||
            source.FindAncestorOfType<ListBoxItem>() is not null ||
            source.FindAncestorOfType<TextBox>() is not null ||
            source.FindAncestorOfType<ScrollBar>() is not null)
        {
            return;
        }

        if (eventArgs.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
            eventArgs.Handled = true;
            return;
        }

        BeginMoveDrag(eventArgs);
        eventArgs.Handled = true;
    }

    private void OnResizeGripPointerPressed(object? sender, PointerPressedEventArgs eventArgs)
    {
        if (WindowState != WindowState.Normal ||
            sender is not Control { Tag: string edgeName } ||
            !Enum.TryParse(edgeName, out WindowEdge edge) ||
            !eventArgs.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        BeginResizeDrag(edge, eventArgs);
        eventArgs.Handled = true;
    }

    private void UpdateMaximizeRestoreIcon()
    {
        if (MaximizeRestoreIcon is null)
        {
            return;
        }

        bool isMaximized = WindowState == WindowState.Maximized;
        ResizeGripOverlay.IsVisible = !isMaximized;
        MaximizeRestoreIcon.Text = isMaximized ? "\uE923" : "\uE922";
        ToolTip.SetTip(
            MaximizeRestoreButton,
            isMaximized ? "Restore" : "Maximize");
    }

    public MainWindow(MainWindowViewModel viewModel)
        : this()
    {
        DataContext = viewModel;
        viewModel.CloseWindowRequested += (_, _) => Close();
        viewModel.SessionEditor.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName == nameof(SessionEditorViewModel.IsEditorOpen) &&
                viewModel.SessionEditor.IsEditorOpen)
            {
                FocusEditor(SessionNameEditor);
            }
        };
        viewModel.FolderEditor.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName == nameof(FolderEditorViewModel.IsFolderEditorOpen) &&
                viewModel.FolderEditor.IsFolderEditorOpen)
            {
                FocusEditor(FolderPathEditor);
            }
        };
        viewModel.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName == nameof(MainWindowViewModel.IsSidebarVisible))
            {
                UpdateSidebarVisibility(viewModel.IsSidebarVisible);
            }
        };
        Opened += (_, _) =>
        {
            RestoreWindowState(viewModel.WindowSettings);
            viewModel.ShowFirstRunSetup();
        };
        Closing += (_, _) =>
        {
            viewModel.CaptureWindowState(
                Bounds.Width,
                Bounds.Height,
                Position.X,
                Position.Y);

            if (TerminalHost.Parent is Panel terminalParent)
            {
                terminalParent.Children.Remove(TerminalHost);
            }
        };
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

    private static void FocusEditor(TextBox editor)
    {
        Dispatcher.UIThread.Post(
            () =>
            {
                editor.Focus();
                editor.SelectAll();
            },
            DispatcherPriority.Loaded);
    }

    private void UpdateSidebarVisibility(bool isVisible)
    {
        if (!isVisible)
        {
            if (SidebarColumn.ActualWidth > 0)
            {
                sidebarWidth = SidebarColumn.ActualWidth;
            }

            SidebarColumn.MinWidth = 0;
            SidebarColumn.MaxWidth = 0;
            SidebarColumn.Width = new GridLength(0);
            SidebarSplitterColumn.Width = new GridLength(0);
            return;
        }

        SidebarColumn.MinWidth = sidebarMinimumWidth;
        SidebarColumn.MaxWidth = sidebarMaximumWidth;
        SidebarColumn.Width = new GridLength(sidebarWidth);
        SidebarSplitterColumn.Width = sidebarSplitterWidth;
    }

    private void OnSessionTreePointerPressed(object? sender, PointerPressedEventArgs eventArgs)
    {
        PointerPoint point = eventArgs.GetCurrentPoint(SessionsTree);
        bool isLeftClick = point.Properties.IsLeftButtonPressed;
        bool isRightClick = point.Properties.IsRightButtonPressed;
        if ((!isLeftClick && !isRightClick) || eventArgs.Source is not Visual source)
        {
            return;
        }

        TreeViewItem? item = source.FindAncestorOfType<TreeViewItem>();
        if (item?.DataContext is not SessionTreeNodeViewModel node)
        {
            return;
        }

        bool controlPressed = eventArgs.KeyModifiers.HasFlag(KeyModifiers.Control);
        if (isRightClick)
        {
            ClearDragStart();
            return;
        }

        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        if (controlPressed && node.IsFolder)
        {
            viewModel.Explorer.ToggleFolderDeletionSelection(node);
            eventArgs.Handled = true;
        }
        else
        {
            viewModel.Explorer.SelectSingleTreeNode(node);
            SelectOnly(item);
        }

        if (isLeftClick && item.DataContext is SessionTreeNodeViewModel { IsSession: true } sessionNode)
        {
            sessionDragStartEvent = eventArgs;
            sessionDragStartPoint = eventArgs.GetPosition(SessionsTree);
            draggedSessionNode = sessionNode;
        }
        else
        {
            ClearDragStart();
        }

        bool clickedExpander = source is ToggleButton ||
            source.FindAncestorOfType<ToggleButton>() is not null;
        if (isLeftClick && !controlPressed && !clickedExpander &&
            item.DataContext is SessionTreeNodeViewModel { IsFolder: true })
        {
            item.IsExpanded = !item.IsExpanded;
            eventArgs.Handled = true;
        }
    }

    private void SelectOnly(TreeViewItem item)
    {
        SessionsTree.SelectedItems?.Clear();
        item.IsSelected = true;
    }

    private void OnSessionTreeItemContextRequested(
        object? sender,
        ContextRequestedEventArgs eventArgs)
    {
        if (sender is not Control target)
        {
            return;
        }

        if (DataContext is MainWindowViewModel viewModel &&
            viewModel.Explorer.HasMultipleFoldersSelected)
        {
            var multipleSelectionFlyout = new MenuFlyout();
            multipleSelectionFlyout.Items.Add(new MenuItem
            {
                Header = "Delete folders",
                Command = viewModel.Explorer.RequestDeleteSelectedFoldersCommand,
            });
            multipleSelectionFlyout.ShowAt(target, showAtPointer: true);
            eventArgs.Handled = true;
            return;
        }

        if (target is
            {
                ContextFlyout: PopupFlyoutBase flyout,
            })
        {
            flyout.ShowAt(target, showAtPointer: true);
            eventArgs.Handled = true;
        }
    }

    private async void OnSessionTreePointerMoved(object? sender, PointerEventArgs eventArgs)
    {
        if (sessionDragStartEvent is null || draggedSessionNode?.Session is null ||
            !eventArgs.GetCurrentPoint(SessionsTree).Properties.IsLeftButtonPressed)
        {
            return;
        }

        Point currentPoint = eventArgs.GetPosition(SessionsTree);
        if (Math.Abs(currentPoint.X - sessionDragStartPoint.X) < 5 &&
            Math.Abs(currentPoint.Y - sessionDragStartPoint.Y) < 5)
        {
            return;
        }

        PointerPressedEventArgs dragEvent = sessionDragStartEvent;
        Guid sessionId = draggedSessionNode.Session.Id;
        ClearDragStart();
        dragEvent.PreventGestureRecognition();
        dragEvent.Pointer.Capture(null);

        var data = new DataTransfer();
        data.Add(DataTransferItem.Create(SessionDragFormat, sessionId.ToString("D")));
        await DragDrop.DoDragDropAsync(dragEvent, data, DragDropEffects.Move);
        ClearDropTarget();
    }

    private void OnSessionTreePointerReleased(object? sender, PointerReleasedEventArgs eventArgs) =>
        ClearDragStart();

    private void OnSessionTreeDragOver(object? sender, DragEventArgs eventArgs)
    {
        if (!eventArgs.DataTransfer.Contains(SessionDragFormat))
        {
            eventArgs.DragEffects = DragDropEffects.None;
            return;
        }

        SessionTreeNodeViewModel? targetNode = GetTreeNode(eventArgs.Source);
        bool canDrop = targetNode is null || targetNode.IsFolder;
        eventArgs.DragEffects = canDrop ? DragDropEffects.Move : DragDropEffects.None;
        SetDropTarget(canDrop && targetNode?.IsFolder == true
            ? GetTreeItem(eventArgs.Source)
            : null);
        eventArgs.Handled = true;
    }

    private void OnSessionTreeDragLeave(object? sender, DragEventArgs eventArgs) =>
        ClearDropTarget();

    private async void OnSessionTreeDrop(object? sender, DragEventArgs eventArgs)
    {
        ClearDropTarget();
        if (!eventArgs.DataTransfer.Contains(SessionDragFormat))
        {
            return;
        }

        SessionTreeNodeViewModel? targetNode = GetTreeNode(eventArgs.Source);
        if (targetNode?.IsSession == true)
        {
            eventArgs.DragEffects = DragDropEffects.None;
            return;
        }

        string? sessionIdValue = eventArgs.DataTransfer.TryGetValue(SessionDragFormat);
        if (!Guid.TryParse(sessionIdValue, out Guid sessionId))
        {
            eventArgs.DragEffects = DragDropEffects.None;
            return;
        }
        string destinationFolder = targetNode?.Path ?? string.Empty;
        if (DataContext is MainWindowViewModel viewModel)
        {
            await viewModel.Explorer.MoveSessionAsync(sessionId, destinationFolder);
            eventArgs.DragEffects = DragDropEffects.Move;
            eventArgs.Handled = true;
        }
    }

    private static TreeViewItem? GetTreeItem(object? source) =>
        source as TreeViewItem ?? (source as Visual)?.FindAncestorOfType<TreeViewItem>();

    private static SessionTreeNodeViewModel? GetTreeNode(object? source) =>
        GetTreeItem(source)?.DataContext as SessionTreeNodeViewModel;

    private void SetDropTarget(TreeViewItem? target)
    {
        if (ReferenceEquals(currentDropTarget, target))
        {
            return;
        }

        ClearDropTarget();
        currentDropTarget = target;
        currentDropTarget?.Classes.Add("dropTarget");
    }

    private void ClearDropTarget()
    {
        currentDropTarget?.Classes.Remove("dropTarget");
        currentDropTarget = null;
    }

    private void ClearDragStart()
    {
        sessionDragStartEvent = null;
        draggedSessionNode = null;
    }

    private void OnSessionTreeDoubleTapped(object? sender, TappedEventArgs eventArgs)
    {
        if (eventArgs.Source is not Visual source ||
            source.FindAncestorOfType<TreeViewItem>()?.DataContext is not
                SessionTreeNodeViewModel { IsSession: true })
        {
            return;
        }

        if (DataContext is MainWindowViewModel viewModel &&
            viewModel.Explorer.OpenSelectedSessionCommand.CanExecute(null))
        {
            viewModel.Explorer.OpenSelectedSessionCommand.Execute(null);
            eventArgs.Handled = true;
        }
    }

    private void OnTerminalTabDoubleTapped(object? sender, TappedEventArgs eventArgs)
    {
        if (eventArgs.Source is Visual source &&
            (source is Button || source.FindAncestorOfType<Button>() is not null))
        {
            return;
        }

        if (sender is not Control { DataContext: TerminalTabViewModel tab })
        {
            return;
        }

        tab.BeginRename();
        Dispatcher.UIThread.Post(
            () =>
            {
                TextBox? editor = (sender as Visual)?
                    .GetVisualDescendants()
                    .OfType<TextBox>()
                    .FirstOrDefault(control => control.Name == "TabTitleEditor");
                editor?.Focus();
                editor?.SelectAll();
            },
            DispatcherPriority.Input);
        eventArgs.Handled = true;
    }

    private void OnTerminalTabPointerPressed(
        object? sender,
        PointerPressedEventArgs eventArgs)
    {
        if (!eventArgs.GetCurrentPoint(TerminalTabs).Properties.IsMiddleButtonPressed ||
            eventArgs.Source is not Visual source ||
            source.FindAncestorOfType<ListBoxItem>()?.DataContext is not
                TerminalTabViewModel tab)
        {
            return;
        }

        eventArgs.Handled = true;
        if (tab.CloseCommand.CanExecute(null))
        {
            tab.CloseCommand.Execute(null);
        }
    }

    private void OnTabTitleEditorKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        if (sender is not TextBox { DataContext: TerminalTabViewModel tab })
        {
            return;
        }

        if (eventArgs.Key == Key.Enter)
        {
            tab.CommitRename();
            eventArgs.Handled = true;
        }
        else if (eventArgs.Key == Key.Escape)
        {
            tab.CancelRename();
            eventArgs.Handled = true;
        }
    }

    private void OnTabTitleEditorLostFocus(object? sender, RoutedEventArgs eventArgs)
    {
        if (sender is TextBox { DataContext: TerminalTabViewModel tab } && tab.IsRenaming)
        {
            tab.CommitRename();
        }
    }
}
