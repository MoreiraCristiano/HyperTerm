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
}
