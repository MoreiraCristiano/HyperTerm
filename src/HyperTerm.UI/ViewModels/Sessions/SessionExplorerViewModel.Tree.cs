using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HyperTerm.Core.Entities;

namespace HyperTerm.UI.ViewModels;

public sealed partial class SessionExplorerViewModel
{
    private async Task ApplySearchAfterDelayAsync(CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(SearchDebounceDelay, cancellation.Token);
            if (ReferenceEquals(searchDebounceCancellation, cancellation))
            {
                searchDebounceCancellation = null;
                ApplyFilter(SelectedSession?.Id);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        finally
        {
            cancellation.Dispose();
        }
    }

    private void CancelPendingSearch()
    {
        CancellationTokenSource? cancellation = searchDebounceCancellation;
        searchDebounceCancellation = null;
        cancellation?.Cancel();
    }

    private void ApplyFilter(Guid? sessionToSelect)
    {
        HashSet<string> expandedFolderPaths = GetExpandedFolderPaths(SessionTree);
        if (pendingFolderMove is { } move)
        {
            expandedFolderPaths = RemapExpandedFolderPaths(expandedFolderPaths, move);
            if (expandedFolderPathsBeforeSearch is not null)
            {
                expandedFolderPathsBeforeSearch = RemapExpandedFolderPaths(
                    expandedFolderPathsBeforeSearch,
                    move);
            }

            pendingFolderMove = null;
        }

        string filter = SearchText.Trim();
        bool isSearching = filter.Length > 0;
        if (isSearching)
        {
            expandedFolderPathsBeforeSearch ??= expandedFolderPaths;
        }
        else if (expandedFolderPathsBeforeSearch is not null)
        {
            expandedFolderPaths = expandedFolderPathsBeforeSearch;
            expandedFolderPathsBeforeSearch = null;
        }

        IEnumerable<SessionListItemViewModel> filteredSessions = allSessions;
        if (isSearching)
        {
            filteredSessions = filteredSessions.Where(session =>
                session.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                session.Host.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                session.Folder.Contains(filter, StringComparison.OrdinalIgnoreCase));
        }

        ClearFolderDeletionSelection();
        var newTree = new ObservableCollection<SessionTreeNodeViewModel>();
        var foldersByPath = new Dictionary<string, SessionTreeNodeViewModel>(
            StringComparer.OrdinalIgnoreCase);
        if (!isSearching)
        {
            foreach (SessionFolder folder in allFolders)
            {
                EnsureFolderPath(folder.Path, newTree, foldersByPath, foldersWithItems);
            }
        }

        int visibleSessionCount = 0;
        foreach (SessionListItemViewModel session in filteredSessions)
        {
            EnsureFolderPath(session.Folder, newTree, foldersByPath, foldersWithItems)
                .Add(SessionTreeNodeViewModel.CreateSession(session));
            visibleSessionCount++;
        }

        SortNodes(newTree, rootFoldersDescending);
        if (isSearching)
        {
            SetAllFoldersExpanded(newTree);
        }
        else
        {
            RestoreExpandedFolders(newTree, expandedFolderPaths);
        }
        SessionCountText = visibleSessionCount == 1
            ? "1 session"
            : $"{visibleSessionCount} sessions";
        SessionTreeNodeViewModel? newSelection = sessionToSelect.HasValue
            ? FindSessionNode(newTree, sessionToSelect.Value)
            : null;
        SessionTree = newTree;
        SelectedTreeNode = newSelection;
    }

    private static void SetAllFoldersExpanded(
        IEnumerable<SessionTreeNodeViewModel> nodes)
    {
        foreach (SessionTreeNodeViewModel node in nodes)
        {
            node.IsExpanded = node.IsFolder;
            SetAllFoldersExpanded(node.Children);
        }
    }

    private void RefreshFoldersWithItems()
    {
        foldersWithItems.Clear();
        foreach (SessionFolder folder in allFolders)
        {
            AddParentPaths(folder.Path, foldersWithItems);
        }

        foreach (SessionListItemViewModel session in allSessions)
        {
            AddParentPaths(session.Folder, foldersWithItems);
            string normalizedPath = NormalizeFolderPath(session.Folder);
            if (normalizedPath.Length > 0)
            {
                foldersWithItems.Add(normalizedPath);
            }
        }
    }

    private static void AddParentPaths(string folderPath, HashSet<string> paths)
    {
        string[] segments = GetFolderPathSegments(folderPath);
        for (int length = 1; length < segments.Length; length++)
        {
            paths.Add(string.Join('/', segments.Take(length)));
        }
    }

    private static string NormalizeFolderPath(string folderPath) =>
        string.Join('/', GetFolderPathSegments(folderPath));

    private static string[] GetFolderPathSegments(string folderPath) =>
        folderPath.Replace('\\', '/').Split(
            '/',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static HashSet<string> RemapExpandedFolderPaths(
        IEnumerable<string> paths,
        (string CurrentPath, string NewPath, string DestinationPath) move)
    {
        var remappedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in paths)
        {
            remappedPaths.Add(path.Equals(move.CurrentPath, StringComparison.OrdinalIgnoreCase)
                ? move.NewPath
                : path.StartsWith($"{move.CurrentPath}/", StringComparison.OrdinalIgnoreCase)
                    ? $"{move.NewPath}{path[move.CurrentPath.Length..]}"
                    : path);
        }

        if (!string.IsNullOrWhiteSpace(move.DestinationPath))
        {
            remappedPaths.Add(move.DestinationPath);
        }

        return remappedPaths;
    }

    private static HashSet<string> GetExpandedFolderPaths(
        IEnumerable<SessionTreeNodeViewModel> nodes)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (SessionTreeNodeViewModel node in nodes)
        {
            if (node.IsFolder && node.IsExpanded)
            {
                paths.Add(node.Path);
            }

            paths.UnionWith(GetExpandedFolderPaths(node.Children));
        }

        return paths;
    }

    private static void RestoreExpandedFolders(
        IEnumerable<SessionTreeNodeViewModel> nodes,
        ISet<string> expandedFolderPaths)
    {
        foreach (SessionTreeNodeViewModel node in nodes)
        {
            node.IsExpanded = node.IsFolder && expandedFolderPaths.Contains(node.Path);
            RestoreExpandedFolders(node.Children, expandedFolderPaths);
        }
    }

    private static ObservableCollection<SessionTreeNodeViewModel> EnsureFolderPath(
        string folderPath,
        ObservableCollection<SessionTreeNodeViewModel> rootNodes,
        Dictionary<string, SessionTreeNodeViewModel> foldersByPath,
        HashSet<string> foldersWithItems)
    {
        string[] segments = GetFolderPathSegments(folderPath);
        ObservableCollection<SessionTreeNodeViewModel> parentNodes = rootNodes;
        string currentPath = string.Empty;
        foreach (string segment in segments)
        {
            currentPath = currentPath.Length == 0 ? segment : $"{currentPath}/{segment}";
            if (!foldersByPath.TryGetValue(currentPath, out SessionTreeNodeViewModel? folderNode))
            {
                folderNode = SessionTreeNodeViewModel.CreateFolder(
                    segment,
                    currentPath,
                    foldersWithItems.Contains(currentPath));
                foldersByPath.Add(currentPath, folderNode);
                parentNodes.Add(folderNode);
            }

            parentNodes = folderNode.Children;
        }

        return parentNodes;
    }

    private static void SortNodes(
        ObservableCollection<SessionTreeNodeViewModel> nodes,
        bool foldersDescending = false)
    {
        foreach (SessionTreeNodeViewModel node in nodes)
        {
            SortNodes(node.Children);
        }

        IEnumerable<SessionTreeNodeViewModel> folders = nodes.Where(node => node.IsFolder);
        folders = foldersDescending
            ? folders.OrderByDescending(node => node.Name, StringComparer.CurrentCultureIgnoreCase)
            : folders.OrderBy(node => node.Name, StringComparer.CurrentCultureIgnoreCase);
        SessionTreeNodeViewModel[] sortedNodes = folders
            .Concat(nodes.Where(node => node.IsSession)
                .OrderBy(node => node.Name, StringComparer.CurrentCultureIgnoreCase))
            .ToArray();
        nodes.Clear();
        foreach (SessionTreeNodeViewModel node in sortedNodes)
        {
            nodes.Add(node);
        }
    }

    private static SessionTreeNodeViewModel? FindSessionNode(
        IEnumerable<SessionTreeNodeViewModel> nodes,
        Guid sessionId)
    {
        foreach (SessionTreeNodeViewModel node in nodes)
        {
            if (node.Session?.Id == sessionId)
            {
                return node;
            }

            SessionTreeNodeViewModel? match = FindSessionNode(node.Children, sessionId);
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }
}
