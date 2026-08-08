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
            canDrop = DragDropInteractionRules.CanMoveFolder(
                draggedPath,
                targetNode?.Path ?? string.Empty);
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
                if (!DragDropInteractionRules.CanMoveFolder(
                        currentPath,
                        destinationFolder))
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
}
