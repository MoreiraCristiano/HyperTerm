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
    private void OnTerminalMenuOpening(object? sender, EventArgs eventArgs)
    {
        if (sender is not MenuFlyout menu ||
            menu.Items.OfType<Separator>().FirstOrDefault() is not { } separator)
        {
            return;
        }

        while (menu.Items.Count > 0 &&
               !ReferenceEquals(menu.Items[0], separator))
        {
            menu.Items.RemoveAt(0);
        }

        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        int index = 0;
        foreach (TerminalLaunchProfileViewModel profile in viewModel.Workspace.TerminalProfiles)
        {
            var item = new MenuItem
            {
                Header = GetTerminalProfileMenuHeader(profile),
                Command = viewModel.Workspace.OpenTerminalProfileCommand,
                CommandParameter = profile,
                IsEnabled = profile.IsAvailable,
            };
            menu.Items.Insert(index++, item);
        }
    }

    internal static string GetTerminalProfileMenuHeader(
        TerminalLaunchProfileViewModel profile) => profile.Name;

    private void OnTerminalTabsLayoutUpdated(object? sender, EventArgs eventArgs)
    {
        if (DataContext is not MainWindowViewModel viewModel ||
            viewModel.Workspace.Tabs.Count == 0 ||
            TabScrollViewer.Viewport.Width <= 0)
        {
            return;
        }

        double width = DragDropInteractionRules.GetAdaptiveTabWidth(
            TabScrollViewer.Viewport.Width - TabActions.Bounds.Width,
            viewModel.Workspace.Tabs.Count);
        foreach (ListBoxItem item in TerminalTabs
                     .GetVisualDescendants()
                     .OfType<ListBoxItem>())
        {
            if (!item.Width.Equals(width))
            {
                item.Width = width;
            }
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

    private void OnTerminalTabPointerWheelChanged(
        object? sender,
        PointerWheelEventArgs eventArgs)
    {
        double maximumOffset = Math.Max(
            0,
            TabScrollViewer.Extent.Width - TabScrollViewer.Viewport.Width);
        double nextOffset = DragDropInteractionRules.GetTabWheelScrollOffset(
            TabScrollViewer.Offset.X,
            maximumOffset,
            eventArgs.Delta.X,
            eventArgs.Delta.Y);
        if (nextOffset.Equals(TabScrollViewer.Offset.X))
        {
            return;
        }

        TabScrollViewer.Offset = new Vector(nextOffset, TabScrollViewer.Offset.Y);
        eventArgs.Handled = true;
    }

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

        bool insertAfter = DragDropInteractionRules.InsertTabAfter(
            eventArgs.GetPosition(target).X,
            target.Bounds.Width);
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

        ListBoxItem target = GetTabItem(eventArgs.Source)!;
        bool insertAfter = DragDropInteractionRules.InsertTabAfter(
            eventArgs.GetPosition(target).X,
            target.Bounds.Width);
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
        tabAutoScrollDirection = DragDropInteractionRules.GetTabAutoScrollDirection(
            pointerX,
            TabScrollViewer.Bounds.Width);
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
