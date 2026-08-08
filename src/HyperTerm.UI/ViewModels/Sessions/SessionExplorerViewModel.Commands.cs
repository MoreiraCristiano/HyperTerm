using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HyperTerm.Core.Entities;

namespace HyperTerm.UI.ViewModels;

public sealed partial class SessionExplorerViewModel
{
    [RelayCommand(CanExecute = nameof(HasSelectedSession))]
    private void OpenSelectedSession() => SessionOpenRequested?.Invoke(SelectedSession!);

    [RelayCommand]
    private void NewSession() => NewSessionRequested?.Invoke(string.Empty);

    [RelayCommand(CanExecute = nameof(HasSelectedFolder))]
    private void NewSessionInSelectedFolder() =>
        NewSessionRequested?.Invoke(SelectedTreeNode!.Path);

    [RelayCommand]
    private void ContextNewSession(SessionTreeNodeViewModel? node)
    {
        if (node?.IsFolder == true)
        {
            NewSessionRequested?.Invoke(node.Path);
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelectedSession))]
    private void EditSession() => EditSessionRequested?.Invoke(SelectedSession!);

    [RelayCommand]
    private void ContextEditSession(SessionTreeNodeViewModel? node)
    {
        if (node?.Session is not null)
        {
            EditSessionRequested?.Invoke(node.Session);
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelectedSession))]
    private void RequestDeleteSession() => DeleteSessionRequested?.Invoke(SelectedSession!);

    [RelayCommand]
    private void ContextDeleteSession(SessionTreeNodeViewModel? node)
    {
        if (node?.Session is not null)
        {
            DeleteSessionRequested?.Invoke(node.Session);
        }
    }

    [RelayCommand]
    private void OpenFolderEditor() => NewFolderRequested?.Invoke(string.Empty);

    [RelayCommand(CanExecute = nameof(HasSelectedFolder))]
    private void OpenSubfolderEditor() => NewFolderRequested?.Invoke($"{SelectedTreeNode!.Path}/");

    [RelayCommand]
    private void ContextNewFolder(SessionTreeNodeViewModel? node)
    {
        if (node?.IsFolder == true)
        {
            NewFolderRequested?.Invoke($"{node.Path}/");
        }
    }

    [RelayCommand]
    private void ContextEditFolder(SessionTreeNodeViewModel? node)
    {
        if (node?.IsFolder == true)
        {
            EditFolderRequested?.Invoke(node.Path);
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelectedFolder))]
    private void RequestDeleteFolder() => DeleteFoldersRequested?.Invoke(
        selectedFolderPaths.Count > 0
            ? selectedFolderPaths.ToArray()
            : [SelectedTreeNode!.Path]);

    [RelayCommand]
    private void RequestDeleteSelectedFolders()
    {
        if (selectedFolderPaths.Count > 1)
        {
            DeleteFoldersRequested?.Invoke(selectedFolderPaths.ToArray());
        }
    }

    [RelayCommand]
    private void ContextDeleteFolder(SessionTreeNodeViewModel? node)
    {
        if (node?.IsFolder == true)
        {
            DeleteFoldersRequested?.Invoke(
                node.IsSelectedForDeletion && selectedFolderPaths.Count > 0
                    ? selectedFolderPaths.ToArray()
                    : [node.Path]);
        }
    }

    [RelayCommand]
    private void ToggleRootFolderSort()
    {
        rootFoldersDescending = !rootFoldersDescending;
        OnPropertyChanged(nameof(RootFolderSortGlyph));
        OnPropertyChanged(nameof(RootFolderSortTooltip));
        CancelPendingSearch();
        ApplyFilter(SelectedSession?.Id);
    }

    private bool HasSelectedSession() => SelectedSession is not null;
    private bool HasSelectedFolder() => SelectedTreeNode?.IsFolder == true;

    private void SetFolderDeletionSelection(SessionTreeNodeViewModel node, bool selected)
    {
        node.IsSelectedForDeletion = selected;
        if (selected)
        {
            selectedFolderNodes.Add(node);
            selectedFolderPaths.Add(node.Path);
        }
        else
        {
            selectedFolderNodes.Remove(node);
            selectedFolderPaths.Remove(node.Path);
        }
    }

    private void ClearFolderDeletionSelection()
    {
        foreach (SessionTreeNodeViewModel node in selectedFolderNodes)
        {
            node.IsSelectedForDeletion = false;
        }

        selectedFolderNodes.Clear();
        selectedFolderPaths.Clear();
    }
}
