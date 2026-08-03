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
    private static readonly DataFormat<string> FolderDragFormat =
        DataFormat.CreateInProcessFormat<string>("HyperTerm.FolderPath");
    private static readonly DataFormat<string> TabDragFormat =
        DataFormat.CreateInProcessFormat<string>("HyperTerm.TabId");

    private PointerPressedEventArgs? sessionDragStartEvent;
    private Point sessionDragStartPoint;
    private SessionTreeNodeViewModel? draggedTreeNode;
    private TreeViewItem? currentDropTarget;
    private PointerPressedEventArgs? tabDragStartEvent;
    private Point tabDragStartPoint;
    private TerminalTabViewModel? draggedTab;
    private ListBoxItem? currentTabDropTarget;
    private bool currentTabDropAfter;
    private double tabAutoScrollDirection;
    private int activationFocusGeneration;
    private readonly DispatcherTimer tabAutoScrollTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(30),
    };
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
        TerminalTabs.AddHandler(
            InputElement.PointerMovedEvent,
            OnTerminalTabPointerMoved,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        TerminalTabs.AddHandler(
            InputElement.PointerReleasedEvent,
            OnTerminalTabPointerReleased,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        DragDrop.SetAllowDrop(TerminalTabs, true);
        DragDrop.AddDragOverHandler(TerminalTabs, OnTerminalTabDragOver);
        DragDrop.AddDragLeaveHandler(TerminalTabs, OnTerminalTabDragLeave);
        DragDrop.AddDropHandler(TerminalTabs, OnTerminalTabDrop);
        tabAutoScrollTimer.Tick += OnTabAutoScrollTick;
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
        Activated += (_, _) =>
        {
            int generation = ++activationFocusGeneration;
            Dispatcher.UIThread.Post(
                () =>
                {
                    if (generation == activationFocusGeneration &&
                        IsActive &&
                        viewModel.AreTerminalHostsVisible)
                    {
                        TerminalHost.FocusAfterWindowActivation();
                    }
                },
                DispatcherPriority.Background);
        };
        Deactivated += (_, _) =>
        {
            activationFocusGeneration++;
            TerminalHost.CancelWindowActivationFocus();
        };
        viewModel.InitializationCompleted += (_, _) =>
        {
            RestoreWindowState(viewModel.WindowSettings);
            if (viewModel.IsInitialized)
            {
                viewModel.ShowFirstRunSetup();
            }
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

        if (isLeftClick && !controlPressed)
        {
            sessionDragStartEvent = eventArgs;
            sessionDragStartPoint = eventArgs.GetPosition(SessionsTree);
            draggedTreeNode = node;
        }
        else
        {
            ClearDragStart();
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
        if (sessionDragStartEvent is null || draggedTreeNode is null ||
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
        SessionTreeNodeViewModel draggedNode = draggedTreeNode;
        ClearDragStart();
        dragEvent.PreventGestureRecognition();
        dragEvent.Pointer.Capture(null);

        var data = new DataTransfer();
        if (draggedNode.Session is { } session)
        {
            data.Add(DataTransferItem.Create(SessionDragFormat, session.Id.ToString("D")));
        }
        else
        {
            data.Add(DataTransferItem.Create(FolderDragFormat, draggedNode.Path));
        }
        await DragDrop.DoDragDropAsync(dragEvent, data, DragDropEffects.Move);
        ClearDropTarget();
    }

    private void OnSessionTreePointerReleased(object? sender, PointerReleasedEventArgs eventArgs)
    {
        if (sessionDragStartEvent is not null &&
            draggedTreeNode is { IsFolder: true } folderNode &&
            eventArgs.Source is Visual source &&
            source.FindAncestorOfType<TreeViewItem>() is { } item &&
            ReferenceEquals(item.DataContext, folderNode))
        {
            Point releasePoint = eventArgs.GetPosition(SessionsTree);
            if (Math.Abs(releasePoint.X - sessionDragStartPoint.X) < 5 &&
                Math.Abs(releasePoint.Y - sessionDragStartPoint.Y) < 5)
            {
                item.IsExpanded = !item.IsExpanded;
                eventArgs.Handled = true;
            }
        }

        ClearDragStart();
    }

    private void OnSessionTreeDragOver(object? sender, DragEventArgs eventArgs)
    {
        bool isSessionDrag = eventArgs.DataTransfer.Contains(SessionDragFormat);
        bool isFolderDrag = eventArgs.DataTransfer.Contains(FolderDragFormat);
        if (!isSessionDrag && !isFolderDrag)
        {
            eventArgs.DragEffects = DragDropEffects.None;
            return;
        }

        SessionTreeNodeViewModel? targetNode = GetTreeNode(eventArgs.Source);
        bool canDrop = targetNode is null || targetNode.IsFolder;
        if (canDrop && isFolderDrag)
        {
            string draggedPath = eventArgs.DataTransfer.TryGetValue(FolderDragFormat) ?? string.Empty;
            canDrop = targetNode is null ||
                (!targetNode.Path.Equals(draggedPath, StringComparison.OrdinalIgnoreCase) &&
                 !targetNode.Path.StartsWith($"{draggedPath}/", StringComparison.OrdinalIgnoreCase));
        }
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
        bool isSessionDrag = eventArgs.DataTransfer.Contains(SessionDragFormat);
        bool isFolderDrag = eventArgs.DataTransfer.Contains(FolderDragFormat);
        if (!isSessionDrag && !isFolderDrag)
        {
            return;
        }

        SessionTreeNodeViewModel? targetNode = GetTreeNode(eventArgs.Source);
        if (targetNode?.IsSession == true)
        {
            eventArgs.DragEffects = DragDropEffects.None;
            return;
        }

        string destinationFolder = targetNode?.Path ?? string.Empty;
        if (DataContext is MainWindowViewModel viewModel)
        {
            if (isSessionDrag)
            {
                string? sessionIdValue = eventArgs.DataTransfer.TryGetValue(SessionDragFormat);
                if (!Guid.TryParse(sessionIdValue, out Guid sessionId))
                {
                    eventArgs.DragEffects = DragDropEffects.None;
                    return;
                }

                await viewModel.Explorer.MoveSessionAsync(sessionId, destinationFolder);
            }
            else
            {
                string currentPath =
                    eventArgs.DataTransfer.TryGetValue(FolderDragFormat) ?? string.Empty;
                if (currentPath.Length == 0 ||
                    destinationFolder.Equals(currentPath, StringComparison.OrdinalIgnoreCase) ||
                    destinationFolder.StartsWith(
                        $"{currentPath}/",
                        StringComparison.OrdinalIgnoreCase))
                {
                    eventArgs.DragEffects = DragDropEffects.None;
                    return;
                }

                await viewModel.Explorer.MoveFolderAsync(currentPath, destinationFolder);
            }

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
        draggedTreeNode = null;
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
        PointerPoint point = eventArgs.GetCurrentPoint(TerminalTabs);
        if (eventArgs.Source is not Visual source ||
            source.FindAncestorOfType<ListBoxItem>()?.DataContext is not TerminalTabViewModel tab)
        {
            return;
        }

        if (point.Properties.IsMiddleButtonPressed)
        {
            eventArgs.Handled = true;
            if (tab.CloseCommand.CanExecute(null))
            {
                tab.CloseCommand.Execute(null);
            }
            return;
        }

        if (!point.Properties.IsLeftButtonPressed || tab.IsRenaming ||
            source is Button or TextBox ||
            source.FindAncestorOfType<Button>() is not null ||
            source.FindAncestorOfType<TextBox>() is not null)
        {
            return;
        }

        tabDragStartEvent = eventArgs;
        tabDragStartPoint = eventArgs.GetPosition(TerminalTabs);
        draggedTab = tab;
    }

    private async void OnTerminalTabPointerMoved(object? sender, PointerEventArgs eventArgs)
    {
        if (tabDragStartEvent is null || draggedTab is null ||
            !eventArgs.GetCurrentPoint(TerminalTabs).Properties.IsLeftButtonPressed)
        {
            return;
        }

        Point currentPoint = eventArgs.GetPosition(TerminalTabs);
        if (Math.Abs(currentPoint.X - tabDragStartPoint.X) < 5 &&
            Math.Abs(currentPoint.Y - tabDragStartPoint.Y) < 5)
        {
            return;
        }

        PointerPressedEventArgs dragEvent = tabDragStartEvent;
        TerminalTabViewModel tab = draggedTab;
        MainWindowViewModel? viewModel = DataContext as MainWindowViewModel;
        TerminalTabViewModel activeTab = viewModel?.Workspace.SelectedTab ?? tab;
        ClearTabDragStart();
        var data = new DataTransfer();
        data.Add(DataTransferItem.Create(TabDragFormat, tab.Id.ToString("D")));
        try
        {
            await DragDrop.DoDragDropAsync(dragEvent, data, DragDropEffects.Move);
        }
        finally
        {
            ClearTabDropTarget();
            StopTabAutoScroll();
            viewModel?.Workspace.RestoreTabAfterDrag(activeTab);
        }
    }

    private void OnTerminalTabPointerReleased(
        object? sender,
        PointerReleasedEventArgs eventArgs) => ClearTabDragStart();

    private void OnTerminalTabDragOver(object? sender, DragEventArgs eventArgs)
    {
        if (!eventArgs.DataTransfer.Contains(TabDragFormat) ||
            GetTabItem(eventArgs.Source) is not { } target)
        {
            eventArgs.DragEffects = DragDropEffects.None;
            ClearTabDropTarget();
            StopTabAutoScroll();
            return;
        }

        bool insertAfter = eventArgs.GetPosition(target).X >= target.Bounds.Width / 2;
        SetTabDropTarget(target, insertAfter);
        UpdateTabAutoScroll(eventArgs.GetPosition(TabScrollViewer).X);
        eventArgs.DragEffects = DragDropEffects.Move;
        eventArgs.Handled = true;
    }

    private void OnTerminalTabDragLeave(object? sender, DragEventArgs eventArgs)
    {
        ClearTabDropTarget();
        StopTabAutoScroll();
    }

    private void OnTerminalTabDrop(object? sender, DragEventArgs eventArgs)
    {
        string? tabIdValue = eventArgs.DataTransfer.TryGetValue(TabDragFormat);
        if (!Guid.TryParse(tabIdValue, out Guid tabId) ||
            GetTabItem(eventArgs.Source)?.DataContext is not TerminalTabViewModel targetTab ||
            DataContext is not MainWindowViewModel viewModel ||
            viewModel.Workspace.Tabs.FirstOrDefault(tab => tab.Id == tabId) is not { } sourceTab)
        {
            eventArgs.DragEffects = DragDropEffects.None;
            ClearTabDropTarget();
            StopTabAutoScroll();
            return;
        }

        bool insertAfter = eventArgs.GetPosition(GetTabItem(eventArgs.Source)!).X >=
            GetTabItem(eventArgs.Source)!.Bounds.Width / 2;
        viewModel.Workspace.MoveTab(sourceTab, targetTab, insertAfter);
        eventArgs.DragEffects = DragDropEffects.Move;
        eventArgs.Handled = true;
        ClearTabDropTarget();
        StopTabAutoScroll();
    }

    private static ListBoxItem? GetTabItem(object? source) =>
        source as ListBoxItem ?? (source as Visual)?.FindAncestorOfType<ListBoxItem>();

    private void SetTabDropTarget(ListBoxItem target, bool insertAfter)
    {
        if (ReferenceEquals(currentTabDropTarget, target) &&
            currentTabDropAfter == insertAfter)
        {
            return;
        }

        ClearTabDropTarget();
        currentTabDropTarget = target;
        currentTabDropAfter = insertAfter;
        target.Classes.Add(insertAfter ? "tabDropAfter" : "tabDropBefore");
    }

    private void ClearTabDropTarget()
    {
        currentTabDropTarget?.Classes.Remove("tabDropBefore");
        currentTabDropTarget?.Classes.Remove("tabDropAfter");
        currentTabDropTarget = null;
    }

    private void ClearTabDragStart()
    {
        tabDragStartEvent = null;
        draggedTab = null;
    }

    private void UpdateTabAutoScroll(double pointerX)
    {
        const double edgeSize = 32;
        tabAutoScrollDirection = pointerX < edgeSize
            ? -1
            : pointerX > TabScrollViewer.Bounds.Width - edgeSize ? 1 : 0;
        if (tabAutoScrollDirection == 0)
        {
            tabAutoScrollTimer.Stop();
        }
        else if (!tabAutoScrollTimer.IsEnabled)
        {
            tabAutoScrollTimer.Start();
        }
    }

    private void OnTabAutoScrollTick(object? sender, EventArgs eventArgs)
    {
        double maximumOffset = Math.Max(
            0,
            TabScrollViewer.Extent.Width - TabScrollViewer.Viewport.Width);
        double nextOffset = Math.Clamp(
            TabScrollViewer.Offset.X + tabAutoScrollDirection * 12,
            0,
            maximumOffset);
        TabScrollViewer.Offset = new Vector(nextOffset, TabScrollViewer.Offset.Y);
    }

    private void StopTabAutoScroll()
    {
        tabAutoScrollDirection = 0;
        tabAutoScrollTimer.Stop();
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
