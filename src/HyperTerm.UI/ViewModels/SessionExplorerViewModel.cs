using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HyperTerm.Core.Abstractions.Services;
using HyperTerm.Core.Entities;

namespace HyperTerm.UI.ViewModels;

public sealed partial class SessionExplorerViewModel(
    ISessionService sessionService,
    ISessionFolderService sessionFolderService) : ViewModelBase
{
    private static readonly TimeSpan SearchDebounceDelay = TimeSpan.FromMilliseconds(175);
    private readonly List<SessionListItemViewModel> allSessions = [];
    private readonly List<SessionFolder> allFolders = [];
    private readonly HashSet<string> foldersWithItems =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> selectedFolderPaths =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<SessionTreeNodeViewModel> selectedFolderNodes = [];
    private bool rootFoldersDescending;
    private CancellationTokenSource? searchDebounceCancellation;
    private (string CurrentPath, string NewPath, string DestinationPath)? pendingFolderMove;
    private HashSet<string>? expandedFolderPathsBeforeSearch;

    public event Action<SessionListItemViewModel>? SessionOpenRequested;
    public event Action<string>? NewSessionRequested;
    public event Action<SessionListItemViewModel>? EditSessionRequested;
    public event Action<SessionListItemViewModel>? DeleteSessionRequested;
    public event Action<string>? NewFolderRequested;
    public event Action<string>? EditFolderRequested;
    public event Action<string[]>? DeleteFoldersRequested;
    public event Action<IReadOnlyList<SessionListItemViewModel>>? SessionsReloaded;
    public event Action<string>? StatusRequested;

    public IReadOnlyList<SessionListItemViewModel> Sessions => allSessions;
    public bool HasMultipleFoldersSelected => selectedFolderPaths.Count > 1;
    public string RootFolderSortGlyph => rootFoldersDescending ? "\uE74B" : "\uE74A";
    public string RootFolderSortTooltip => rootFoldersDescending
        ? "Sort root folders ascending"
        : "Sort root folders descending";

    [ObservableProperty]
    private string sessionCountText = "0 sessions";

    [ObservableProperty]
    private string searchText = string.Empty;

    [ObservableProperty]
    private ObservableCollection<SessionTreeNodeViewModel> sessionTree = [];

    [ObservableProperty]
    private SessionListItemViewModel? selectedSession;

    [ObservableProperty]
    private SessionTreeNodeViewModel? selectedTreeNode;

    partial void OnSearchTextChanged(string value)
    {
        CancelPendingSearch();
        if (string.IsNullOrWhiteSpace(value))
        {
            ApplyFilter(SelectedSession?.Id);
            return;
        }

        var cancellation = new CancellationTokenSource();
        searchDebounceCancellation = cancellation;
        _ = ApplySearchAfterDelayAsync(cancellation);
    }

    partial void OnSelectedTreeNodeChanged(SessionTreeNodeViewModel? value)
    {
        SelectedSession = value?.Session;
        NewSessionInSelectedFolderCommand.NotifyCanExecuteChanged();
        OpenSubfolderEditorCommand.NotifyCanExecuteChanged();
        RequestDeleteFolderCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedSessionChanged(SessionListItemViewModel? value)
    {
        OpenSelectedSessionCommand.NotifyCanExecuteChanged();
        EditSessionCommand.NotifyCanExecuteChanged();
        RequestDeleteSessionCommand.NotifyCanExecuteChanged();
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default) =>
        ReloadAsync(null, cancellationToken);

    public async Task ReloadAsync(
        Guid? sessionToSelect = null,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Session> sessions = await sessionService.GetAllAsync(cancellationToken);
        IReadOnlyList<SessionFolder> folders =
            await sessionFolderService.GetAllAsync(cancellationToken);
        allSessions.Clear();
        allSessions.AddRange(sessions.Select(session => new SessionListItemViewModel(session)));
        allFolders.Clear();
        allFolders.AddRange(folders);
        RefreshFoldersWithItems();
        CancelPendingSearch();
        ApplyFilter(sessionToSelect);
        SessionsReloaded?.Invoke(allSessions);
        if (sessions.Count == 0)
        {
            StatusRequested?.Invoke("No sessions available");
        }
    }

    public void SelectSingleTreeNode(SessionTreeNodeViewModel node)
    {
        ArgumentNullException.ThrowIfNull(node);
        ClearFolderDeletionSelection();
        SelectedTreeNode = node;
    }

    public void ToggleFolderDeletionSelection(SessionTreeNodeViewModel node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (!node.IsFolder)
        {
            return;
        }

        if (selectedFolderNodes.Count == 0 &&
            SelectedTreeNode is { IsFolder: true } currentFolder &&
            !ReferenceEquals(currentFolder, node))
        {
            SetFolderDeletionSelection(currentFolder, true);
        }

        SetFolderDeletionSelection(node, !node.IsSelectedForDeletion);
        SelectedTreeNode = null;
    }

    public async Task MoveSessionAsync(Guid sessionId, string destinationFolder)
    {
        try
        {
            Session session = await sessionService.MoveAsync(sessionId, destinationFolder);
            StatusRequested?.Invoke(string.IsNullOrWhiteSpace(session.Folder)
                ? $"Session ‘{session.Name}’ moved to root"
                : $"Session ‘{session.Name}’ moved to ‘{session.Folder}’");
            await ReloadAsync(session.Id);
        }
        catch (KeyNotFoundException exception)
        {
            StatusRequested?.Invoke(exception.Message);
            await ReloadAsync();
        }
    }

    public async Task MoveFolderAsync(string currentPath, string destinationFolder)
    {
        string folderName = currentPath.Split('/').Last();
        string newPath = string.IsNullOrWhiteSpace(destinationFolder)
            ? folderName
            : $"{destinationFolder}/{folderName}";
        if (currentPath.Equals(newPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            await sessionFolderService.RenameAsync(currentPath, newPath);
            pendingFolderMove = (currentPath, newPath, destinationFolder);
            StatusRequested?.Invoke(string.IsNullOrWhiteSpace(destinationFolder)
                ? $"Folder ‘{folderName}’ moved to root"
                : $"Folder ‘{folderName}’ moved to ‘{destinationFolder}’");
            await ReloadAsync();
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException or KeyNotFoundException)
        {
            StatusRequested?.Invoke(exception.Message);
        }
    }

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
