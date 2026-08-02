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
    private readonly List<SessionListItemViewModel> allSessions = [];
    private readonly List<SessionFolder> allFolders = [];
    private readonly HashSet<string> selectedFolderPaths =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<SessionTreeNodeViewModel> selectedFolderNodes = [];
    private bool rootFoldersDescending;
    private (string CurrentPath, string NewPath, string DestinationPath)? pendingFolderMove;

    public event Action<SessionListItemViewModel>? SessionOpenRequested;
    public event Action<string>? NewSessionRequested;
    public event Action<SessionListItemViewModel>? EditSessionRequested;
    public event Action<SessionListItemViewModel>? DeleteSessionRequested;
    public event Action<string>? NewFolderRequested;
    public event Action<string>? EditFolderRequested;
    public event Action<string[]>? DeleteFoldersRequested;
    public event Action<IReadOnlyList<SessionListItemViewModel>>? SessionsReloaded;
    public event Action<string>? StatusRequested;

    public ObservableCollection<SessionTreeNodeViewModel> SessionTree { get; } = [];
    public ObservableCollection<string> FolderOptions { get; } = [];
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
    private SessionListItemViewModel? selectedSession;

    [ObservableProperty]
    private SessionTreeNodeViewModel? selectedTreeNode;

    partial void OnSearchTextChanged(string value) => ApplyFilter(SelectedSession?.Id);

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
        RefreshFolderOptions();
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

    private void ApplyFilter(Guid? sessionToSelect)
    {
        HashSet<string> expandedFolderPaths = GetExpandedFolderPaths(SessionTree);
        if (pendingFolderMove is { } move)
        {
            expandedFolderPaths = RemapExpandedFolderPaths(expandedFolderPaths, move);
            pendingFolderMove = null;
        }
        string filter = SearchText.Trim();
        IEnumerable<SessionListItemViewModel> filteredSessions = allSessions;
        if (filter.Length > 0)
        {
            filteredSessions = filteredSessions.Where(session =>
                session.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                session.Host.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                session.Folder.Contains(filter, StringComparison.OrdinalIgnoreCase));
        }

        ClearFolderDeletionSelection();
        SessionTree.Clear();
        var foldersByPath = new Dictionary<string, SessionTreeNodeViewModel>(
            StringComparer.OrdinalIgnoreCase);
        foreach (SessionFolder folder in allFolders)
        {
            EnsureFolderPath(folder.Path, foldersByPath);
        }

        int visibleSessionCount = 0;
        foreach (SessionListItemViewModel session in filteredSessions)
        {
            EnsureFolderPath(session.Folder, foldersByPath)
                .Add(SessionTreeNodeViewModel.CreateSession(session));
            visibleSessionCount++;
        }

        SortNodes(SessionTree, rootFoldersDescending);
        RestoreExpandedFolders(SessionTree, expandedFolderPaths);
        SessionCountText = visibleSessionCount == 1
            ? "1 session"
            : $"{visibleSessionCount} sessions";
        SelectedTreeNode = sessionToSelect.HasValue
            ? FindSessionNode(SessionTree, sessionToSelect.Value)
            : null;
    }

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

    private void RefreshFolderOptions()
    {
        string[] paths = allFolders.Select(folder => folder.Path)
            .Concat(allSessions.Select(session => session.Folder))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        FolderOptions.Clear();
        foreach (string path in paths)
        {
            FolderOptions.Add(path);
        }
    }

    private ObservableCollection<SessionTreeNodeViewModel> EnsureFolderPath(
        string folderPath,
        IDictionary<string, SessionTreeNodeViewModel> foldersByPath)
    {
        string[] segments = folderPath.Replace('\\', '/').Split(
            '/',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        ObservableCollection<SessionTreeNodeViewModel> parentNodes = SessionTree;
        string currentPath = string.Empty;
        foreach (string segment in segments)
        {
            currentPath = currentPath.Length == 0 ? segment : $"{currentPath}/{segment}";
            if (!foldersByPath.TryGetValue(currentPath, out SessionTreeNodeViewModel? folderNode))
            {
                folderNode = SessionTreeNodeViewModel.CreateFolder(segment, currentPath);
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
