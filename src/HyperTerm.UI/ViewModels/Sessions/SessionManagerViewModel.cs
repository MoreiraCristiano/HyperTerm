using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HyperTerm.Core.Abstractions.Services;
using HyperTerm.Core.Entities;
using HyperTerm.Core.Models;

namespace HyperTerm.UI.ViewModels;

public sealed partial class SessionManagerViewModel(
    ISessionService sessionService,
    ISessionFolderService sessionFolderService) : ViewModelBase
{
    private static readonly SessionManagerSortOption NameSort =
        new("Name", SessionManagerSortField.Name);
    private readonly List<SessionManagerItemViewModel> allSessions = [];
    private SessionManagerItemViewModel? draftSession;
    private SessionManagerItemViewModel? sessionPendingDeletion;

    public event Action<Guid?>? SessionsChanged;
    public event Action<string>? StatusRequested;

    public IReadOnlyList<SessionManagerSortOption> SortOptions { get; } =
    [
        NameSort,
        new("Host", SessionManagerSortField.Host),
        new("Username", SessionManagerSortField.Username),
        new("Folder", SessionManagerSortField.Folder),
        new("Last updated", SessionManagerSortField.UpdatedAt),
    ];

    public ObservableCollection<SessionManagerItemViewModel> Sessions { get; } = [];

    public ObservableCollection<SessionManagerFolderOption> FolderOptions { get; } = [];

    public bool HasEditor => IsCreating || SelectedSession is not null;

    public bool HasNoEditor => !HasEditor;

    public bool IsExistingSession =>
        !IsCreating && SelectedSession is { IsDraft: false };

    public bool HasVisibleSessions => Sessions.Count > 0;

    public bool HasNoVisibleSessions => Sessions.Count == 0;

    public string EditorTitle => IsCreating ? "New session" : "Session details";

    public string SortDirectionGlyph => SortDescending ? "\uE74B" : "\uE74A";

    public string SortDirectionTooltip => SortDescending
        ? "Sort ascending"
        : "Sort descending";

    public string VisibleSessionCountText => Sessions.Count == 1
        ? "1 session"
        : $"{Sessions.Count} sessions";

    public string EmptyListMessage => allSessions.Count == 0
        ? "No SSH sessions saved."
        : "No sessions match the search.";

    public string DeleteConfirmationTitle => sessionPendingDeletion is null
        ? "Delete session?"
        : $"Delete ‘{sessionPendingDeletion.Name}’?";

    [ObservableProperty]
    private bool isOpen;

    [ObservableProperty]
    private bool isDeleteConfirmationOpen;

    [ObservableProperty]
    private string searchText = string.Empty;

    [ObservableProperty]
    private SessionManagerSortOption selectedSortOption = NameSort;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SortDirectionGlyph))]
    [NotifyPropertyChangedFor(nameof(SortDirectionTooltip))]
    private bool sortDescending;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEditor))]
    [NotifyPropertyChangedFor(nameof(HasNoEditor))]
    [NotifyPropertyChangedFor(nameof(IsExistingSession))]
    private SessionManagerItemViewModel? selectedSession;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEditor))]
    [NotifyPropertyChangedFor(nameof(HasNoEditor))]
    [NotifyPropertyChangedFor(nameof(IsExistingSession))]
    [NotifyPropertyChangedFor(nameof(EditorTitle))]
    private bool isCreating;

    [ObservableProperty]
    private string editorName = string.Empty;

    [ObservableProperty]
    private string editorHost = string.Empty;

    [ObservableProperty]
    private decimal editorPort = 22;

    [ObservableProperty]
    private string editorUsername = string.Empty;

    [ObservableProperty]
    private SessionManagerFolderOption? editorFolder;

    [ObservableProperty]
    private string editorNotes = string.Empty;

    [ObservableProperty]
    private string? editorError;

    partial void OnSearchTextChanged(string value) => ApplyView();

    partial void OnSelectedSortOptionChanged(SessionManagerSortOption value) => ApplyView();

    partial void OnSortDescendingChanged(bool value) => ApplyView();

    partial void OnEditorNameChanged(string value) => UpdateDraftFromEditor();

    partial void OnEditorHostChanged(string value) => UpdateDraftFromEditor();

    partial void OnEditorPortChanged(decimal value) => UpdateDraftFromEditor();

    partial void OnEditorUsernameChanged(string value) => UpdateDraftFromEditor();

    partial void OnEditorFolderChanged(SessionManagerFolderOption? value) =>
        UpdateDraftFromEditor();

    partial void OnEditorNotesChanged(string value) => UpdateDraftFromEditor();

    partial void OnSelectedSessionChanged(SessionManagerItemViewModel? value)
    {
        if (value is null)
        {
            if (!IsCreating)
            {
                ClearEditor();
            }

            return;
        }

        if (value.IsDraft)
        {
            IsCreating = true;
            LoadEditor(value);
            return;
        }

        DiscardDraft();
        IsCreating = false;
        LoadEditor(value);
    }

    [RelayCommand]
    private async Task OpenSessionManagerAsync(CancellationToken cancellationToken)
    {
        DiscardDraft();
        IsCreating = false;
        EditorError = null;
        await ReloadAsync(null, cancellationToken);
        IsOpen = true;
    }

    [RelayCommand]
    public void CloseSessionManager()
    {
        IsDeleteConfirmationOpen = false;
        sessionPendingDeletion = null;
        DiscardDraft();
        IsCreating = false;
        SelectedSession = null;
        EditorError = null;
        IsOpen = false;
    }

    [RelayCommand]
    private void ToggleSortDirection() => SortDescending = !SortDescending;

    [RelayCommand]
    private void AddSession()
    {
        if (draftSession is not null)
        {
            SelectedSession = draftSession;
            return;
        }

        SelectedSession = null;
        IsCreating = true;
        ClearEditor();
        draftSession = SessionManagerItemViewModel.CreateDraft();
        ApplyView();
        SelectedSession = draftSession;
    }

    [RelayCommand]
    private async Task SaveSessionAsync(CancellationToken cancellationToken)
    {
        EditorError = null;
        try
        {
            var details = new SessionDetails(
                EditorName,
                EditorHost,
                decimal.ToInt32(EditorPort),
                EditorUsername,
                IsCreating ? null : SelectedSession?.PrivateKey,
                EditorFolder?.Value ?? string.Empty,
                EditorNotes);
            Session savedSession = IsCreating
                ? await sessionService.CreateAsync(details, cancellationToken)
                : await sessionService.UpdateAsync(
                    SelectedSession?.Id ?? throw new InvalidOperationException(
                        "Select a session before saving."),
                    details,
                    cancellationToken);
            bool wasCreating = IsCreating;
            if (wasCreating)
            {
                draftSession = null;
            }

            IsCreating = false;
            await ReloadAsync(savedSession.Id, cancellationToken);
            StatusRequested?.Invoke(wasCreating ? "Session created" : "Session updated");
            SessionsChanged?.Invoke(savedSession.Id);
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException or
                OverflowException or KeyNotFoundException)
        {
            EditorError = exception.Message;
        }
    }

    [RelayCommand(CanExecute = nameof(IsExistingSession))]
    private void RequestDeleteSession()
    {
        sessionPendingDeletion = SelectedSession;
        OnPropertyChanged(nameof(DeleteConfirmationTitle));
        IsDeleteConfirmationOpen = sessionPendingDeletion is not null;
    }

    [RelayCommand]
    public void CancelDeleteSession()
    {
        IsDeleteConfirmationOpen = false;
        sessionPendingDeletion = null;
        OnPropertyChanged(nameof(DeleteConfirmationTitle));
    }

    [RelayCommand]
    private async Task ConfirmDeleteSessionAsync(CancellationToken cancellationToken)
    {
        if (sessionPendingDeletion is null)
        {
            CancelDeleteSession();
            return;
        }

        int removedIndex = Sessions.IndexOf(sessionPendingDeletion);
        Guid? neighborId = Sessions.Count > 1
            ? Sessions[Math.Min(
                removedIndex < Sessions.Count - 1 ? removedIndex + 1 : removedIndex - 1,
                Sessions.Count - 1)].Id
            : null;
        Guid deletedId = sessionPendingDeletion.Id;
        string deletedName = sessionPendingDeletion.Name;
        try
        {
            await sessionService.DeleteAsync(deletedId, cancellationToken);
            CancelDeleteSession();
            await ReloadAsync(neighborId, cancellationToken);
            StatusRequested?.Invoke($"Session ‘{deletedName}’ deleted");
            SessionsChanged?.Invoke(neighborId);
        }
        catch (KeyNotFoundException exception)
        {
            EditorError = exception.Message;
            CancelDeleteSession();
            await ReloadAsync(neighborId, cancellationToken);
            SessionsChanged?.Invoke(neighborId);
        }
    }

    private async Task ReloadAsync(Guid? preferredSessionId, CancellationToken cancellationToken)
    {
        Task<IReadOnlyList<Session>> sessionsTask = sessionService.GetAllAsync(cancellationToken);
        Task<IReadOnlyList<SessionFolder>> foldersTask =
            sessionFolderService.GetAllAsync(cancellationToken);
        await Task.WhenAll(sessionsTask, foldersTask);

        allSessions.Clear();
        allSessions.AddRange(sessionsTask.Result.Select(session =>
            new SessionManagerItemViewModel(session)));
        LoadFolderOptions(foldersTask.Result);
        ApplyView(preferredSessionId);
    }

    private void LoadFolderOptions(IReadOnlyList<SessionFolder> folders)
    {
        string? selectedFolder = EditorFolder?.Value;
        FolderOptions.Clear();
        FolderOptions.Add(new SessionManagerFolderOption("Root", string.Empty));
        IEnumerable<string> folderPaths = folders.Select(folder => folder.Path)
            .Concat(allSessions.Select(session => session.Folder))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.CurrentCultureIgnoreCase);
        foreach (string path in folderPaths)
        {
            FolderOptions.Add(new SessionManagerFolderOption(path, path));
        }

        EditorFolder = FindFolderOption(selectedFolder);
    }

    private void ApplyView(Guid? preferredSessionId = null)
    {
        Guid? selectionId = preferredSessionId ?? SelectedSession?.Id;
        string search = SearchText.Trim();
        IEnumerable<SessionManagerItemViewModel> filtered = allSessions;
        if (search.Length > 0)
        {
            filtered = filtered.Where(session =>
                session.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                session.Host.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                session.Username.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                session.Folder.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                session.Notes?.Contains(search, StringComparison.OrdinalIgnoreCase) == true);
        }

        SessionManagerItemViewModel[] visibleSessions = Sort(filtered).ToArray();
        Sessions.Clear();
        if (draftSession is not null)
        {
            Sessions.Add(draftSession);
        }

        foreach (SessionManagerItemViewModel session in visibleSessions)
        {
            Sessions.Add(session);
        }

        OnPropertyChanged(nameof(VisibleSessionCountText));
        OnPropertyChanged(nameof(HasVisibleSessions));
        OnPropertyChanged(nameof(HasNoVisibleSessions));
        OnPropertyChanged(nameof(EmptyListMessage));
        SessionManagerItemViewModel? nextSelection = selectionId.HasValue
            ? Sessions.FirstOrDefault(session => session.Id == selectionId.Value)
            : null;
        if (!IsCreating)
        {
            SelectedSession = nextSelection ?? visibleSessions.FirstOrDefault();
        }
    }

    private IEnumerable<SessionManagerItemViewModel> Sort(
        IEnumerable<SessionManagerItemViewModel> sessions)
    {
        StringComparer comparer = StringComparer.CurrentCultureIgnoreCase;
        IOrderedEnumerable<SessionManagerItemViewModel> ordered =
            (SelectedSortOption.Field, SortDescending) switch
            {
                (SessionManagerSortField.Host, false) =>
                    sessions.OrderBy(session => session.Host, comparer),
                (SessionManagerSortField.Host, true) =>
                    sessions.OrderByDescending(session => session.Host, comparer),
                (SessionManagerSortField.Username, false) =>
                    sessions.OrderBy(session => session.Username, comparer),
                (SessionManagerSortField.Username, true) =>
                    sessions.OrderByDescending(session => session.Username, comparer),
                (SessionManagerSortField.Folder, false) =>
                    sessions.OrderBy(session => session.Folder, comparer),
                (SessionManagerSortField.Folder, true) =>
                    sessions.OrderByDescending(session => session.Folder, comparer),
                (SessionManagerSortField.UpdatedAt, false) =>
                    sessions.OrderBy(session => session.UpdatedAt),
                (SessionManagerSortField.UpdatedAt, true) =>
                    sessions.OrderByDescending(session => session.UpdatedAt),
                (SessionManagerSortField.Name, true) =>
                    sessions.OrderByDescending(session => session.Name, comparer),
                _ => sessions.OrderBy(session => session.Name, comparer),
            };
        return ordered
            .ThenBy(session => session.Name, comparer)
            .ThenBy(session => session.Id);
    }

    private void LoadEditor(SessionManagerItemViewModel session)
    {
        EditorName = session.Name;
        EditorHost = session.Host;
        EditorPort = session.Port;
        EditorUsername = session.Username;
        EditorFolder = FindFolderOption(session.Folder);
        EditorNotes = session.Notes ?? string.Empty;
        EditorError = null;
        RequestDeleteSessionCommand.NotifyCanExecuteChanged();
    }

    private void ClearEditor()
    {
        EditorName = string.Empty;
        EditorHost = string.Empty;
        EditorPort = 22;
        EditorUsername = string.Empty;
        EditorFolder = FolderOptions.FirstOrDefault();
        EditorNotes = string.Empty;
        EditorError = null;
        RequestDeleteSessionCommand.NotifyCanExecuteChanged();
    }

    private void UpdateDraftFromEditor()
    {
        if (draftSession is null)
        {
            return;
        }

        draftSession.Name = EditorName;
        draftSession.Host = EditorHost;
        draftSession.Port = decimal.ToInt32(EditorPort);
        draftSession.Username = EditorUsername;
        draftSession.Folder = EditorFolder?.Value ?? string.Empty;
        draftSession.Notes = EditorNotes;
    }

    private void DiscardDraft()
    {
        if (draftSession is null)
        {
            return;
        }

        Sessions.Remove(draftSession);
        draftSession = null;
        OnPropertyChanged(nameof(VisibleSessionCountText));
        OnPropertyChanged(nameof(HasVisibleSessions));
        OnPropertyChanged(nameof(HasNoVisibleSessions));
        OnPropertyChanged(nameof(EmptyListMessage));
    }

    private SessionManagerFolderOption? FindFolderOption(string? folder) =>
        FolderOptions.FirstOrDefault(option => option.Value.Equals(
            folder ?? string.Empty,
            StringComparison.OrdinalIgnoreCase)) ?? FolderOptions.FirstOrDefault();
}
