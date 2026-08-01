using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HyperTerm.Core.Abstractions.Services;
using HyperTerm.Core.Abstractions.Settings;
using HyperTerm.Core.Abstractions.Terminal;
using HyperTerm.Core.Entities;
using HyperTerm.Core.Exceptions;
using HyperTerm.Core.Models;
using HyperTerm.UI.Services;

namespace HyperTerm.UI.ViewModels;

public sealed partial class MainWindowViewModel(
    ISessionService sessionService,
    ISessionFolderService sessionFolderService,
    ITerminalSessionFactory terminalSessionFactory,
    IPtySessionFactory ptySessionFactory,
    ISettingsService settingsService,
    IThemeService themeService,
    IExecutableFilePicker executableFilePicker,
    ISessionArchiveService sessionArchiveService,
    ISessionArchiveFilePicker sessionArchiveFilePicker,
    ISystemFontService systemFontService) : ViewModelBase
{
    public event EventHandler? CloseWindowRequested;

    private readonly List<SessionListItemViewModel> allSessions = [];
    private readonly List<SessionFolder> allFolders = [];
    private readonly HashSet<string> selectedFolderPaths =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<SessionTreeNodeViewModel> selectedFolderNodes = [];
    private string[] foldersPendingDeletion = [];
    private SessionListItemViewModel? sessionPendingDeletion;
    private Guid? editingSessionId;
    private string? editingFolderPath;
    private ApplicationSettings applicationSettings = new();
    private bool windowStateChanged;
    private bool rootFoldersDescending;
    private bool requiresInitialPowerShellSelection;

    public string Title => SelectedTab is null
        ? "HyperTerm"
        : $"HyperTerm — {SelectedTab.Title}";

    public IReadOnlyList<string> ThemeOptions { get; } = ["Dark"];

    public WindowSettings WindowSettings => applicationSettings.Window;

    public ObservableCollection<SessionTreeNodeViewModel> SessionTree { get; } = [];

    public ObservableCollection<TerminalTabViewModel> Tabs { get; } = [];

    public ObservableCollection<string> FolderOptions { get; } = [];

    public ObservableCollection<string> SystemFontFamilies { get; } = [];

    public bool HasMultipleFoldersSelected => selectedFolderPaths.Count > 1;

    public string RootFolderSortGlyph => rootFoldersDescending ? "\uE74B" : "\uE74A";

    public string RootFolderSortTooltip => rootFoldersDescending
        ? "Sort root folders ascending"
        : "Sort root folders descending";

    public IReadOnlyList<string> TerminalCursorStyles { get; } =
        ["Bar", "Block", "Underline"];

    public IReadOnlyList<TerminalSelectionColorOption> TerminalSelectionColors { get; } =
    [
        new("Blue", "#264F78"),
        new("Green", "#275D4E"),
        new("Purple", "#5A3D73"),
        new("Orange", "#754C24"),
        new("Red", "#6E3940"),
        new("Silver", "#5B6068"),
    ];

    [ObservableProperty]
    private string sessionCountText = "0 sessions";

    [ObservableProperty]
    private string searchText = string.Empty;

    [ObservableProperty]
    private SessionListItemViewModel? selectedSession;

    [ObservableProperty]
    private SessionTreeNodeViewModel? selectedTreeNode;

    [ObservableProperty]
    private TerminalTabViewModel? selectedTab;

    [ObservableProperty]
    private bool hasOpenTabs;

    public bool IsTabAreaEmpty => !HasOpenTabs;

    public bool AreTerminalHostsVisible =>
        HasOpenTabs && !IsEditorOpen && !IsDeleteConfirmationOpen &&
        !IsSettingsOpen && !IsShortcutsOpen && !IsFolderEditorOpen &&
        !IsFolderDeleteConfirmationOpen && !IsPowerShellSetupOpen;

    [ObservableProperty]
    private string statusText = "Ready";

    [ObservableProperty]
    private string terminalStatusText = "PowerShell";

    [ObservableProperty]
    private bool isStatusBarVisible;

    [ObservableProperty]
    private bool isSidebarVisible = true;

    [ObservableProperty]
    private bool isShortcutsOpen;

    [ObservableProperty]
    private bool isFolderEditorOpen;

    [ObservableProperty]
    private string folderPath = string.Empty;

    [ObservableProperty]
    private string folderEditorTitle = "New folder";

    [ObservableProperty]
    private string folderEditorAction = "Create";

    [ObservableProperty]
    private string? folderError;

    [ObservableProperty]
    private bool isFolderDeleteConfirmationOpen;

    [ObservableProperty]
    private string folderDeleteTitle = "Delete folder?";

    [ObservableProperty]
    private string folderDeleteMessage = string.Empty;

    [ObservableProperty]
    private bool forceDeleteFolders;

    [ObservableProperty]
    private string? folderDeleteError;

    [ObservableProperty]
    private bool isSettingsOpen;

    [ObservableProperty]
    private bool isPowerShellSetupOpen;

    [ObservableProperty]
    private string? powerShellSetupError;

    [ObservableProperty]
    private string settingsPowerShellPath = "pwsh.exe";

    [ObservableProperty]
    private string settingsTheme = "Dark";

    [ObservableProperty]
    private string settingsTerminalFontFamily = "Cascadia Mono";

    [ObservableProperty]
    private decimal settingsTerminalFontSize = 13;

    [ObservableProperty]
    private TerminalSelectionColorOption settingsTerminalSelectionColor =
        new("Blue", "#264F78");

    [ObservableProperty]
    private string settingsTerminalCursorStyle = "Bar";

    [ObservableProperty]
    private bool settingsTerminalCursorBlink = true;

    [ObservableProperty]
    private string? settingsError;

    [ObservableProperty]
    private string? settingsDataStatus;

    public bool HasSettingsDataStatus =>
        !string.IsNullOrWhiteSpace(SettingsDataStatus);

    [ObservableProperty]
    private bool isEditorOpen;

    [ObservableProperty]
    private bool isDeleteConfirmationOpen;

    [ObservableProperty]
    private string editorTitle = "New session";

    [ObservableProperty]
    private string editorName = string.Empty;

    [ObservableProperty]
    private string editorHost = string.Empty;

    [ObservableProperty]
    private decimal editorPort = 22;

    [ObservableProperty]
    private string editorUsername = string.Empty;

    [ObservableProperty]
    private string editorPrivateKey = string.Empty;

    [ObservableProperty]
    private string editorFolder = string.Empty;

    [ObservableProperty]
    private string editorNotes = string.Empty;

    [ObservableProperty]
    private string? editorError;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        requiresInitialPowerShellSelection = !settingsService.Exists();

        try
        {
            applicationSettings = await settingsService.LoadAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(applicationSettings.PowerShellPath))
            {
                applicationSettings = applicationSettings with
                {
                    PowerShellPath = "pwsh.exe",
                };
                await settingsService.SaveAsync(applicationSettings, cancellationToken);
            }
            SettingsPowerShellPath = applicationSettings.PowerShellPath;
            SettingsTheme = NormalizeTheme(applicationSettings.Theme);
            SettingsTerminalFontFamily = applicationSettings.TerminalFontFamily;
            SettingsTerminalFontSize = (decimal)applicationSettings.TerminalFontSize;
            SettingsTerminalSelectionColor = FindSelectionColorOption(
                applicationSettings.TerminalSelectionColor);
            SettingsTerminalCursorStyle = NormalizeCursorStyle(
                applicationSettings.TerminalCursorStyle);
            SettingsTerminalCursorBlink = applicationSettings.TerminalCursorBlink;
            themeService.Apply(SettingsTheme);
            UpdateTerminalStatus();
        }
        catch (Exception exception) when (exception is IOException or System.Text.Json.JsonException)
        {
            SettingsError = $"Failed to load settings: {exception.Message}";
            IsSettingsOpen = true;
        }

        await ReloadSessionsAsync(null, cancellationToken);
        if (!requiresInitialPowerShellSelection)
        {
            await OpenLocalTerminalAsync();
        }
    }

    public void ShowFirstRunSetup()
    {
        if (!requiresInitialPowerShellSelection)
        {
            return;
        }

        PowerShellSetupError = null;
        IsPowerShellSetupOpen = true;
    }

    public async Task ShutdownAsync()
    {
        foreach (TerminalTabViewModel tab in Tabs.ToArray())
        {
            await CloseTabAsync(tab);
        }

        if (windowStateChanged && !requiresInitialPowerShellSelection)
        {
            await settingsService.SaveAsync(applicationSettings);
            windowStateChanged = false;
        }
    }

    public void CaptureWindowState(double width, double height, int x, int y)
    {
        applicationSettings = applicationSettings with
        {
            Window = new WindowSettings
            {
                Width = Math.Max(900, width),
                Height = Math.Max(600, height),
                X = x,
                Y = y,
            },
        };
        windowStateChanged = true;
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter(SelectedSession?.Id);

    partial void OnSelectedTreeNodeChanged(SessionTreeNodeViewModel? value)
    {
        SelectedSession = value?.Session;
        EditFolderCommand.NotifyCanExecuteChanged();
        NewSessionInSelectedFolderCommand.NotifyCanExecuteChanged();
        OpenSubfolderEditorCommand.NotifyCanExecuteChanged();
        RequestDeleteFolderCommand.NotifyCanExecuteChanged();
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

    partial void OnSelectedSessionChanged(SessionListItemViewModel? value)
    {
        OpenSelectedSessionCommand.NotifyCanExecuteChanged();
        EditSessionCommand.NotifyCanExecuteChanged();
        RequestDeleteSessionCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedTabChanged(
        TerminalTabViewModel? oldValue,
        TerminalTabViewModel? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.PropertyChanged -= OnSelectedTabPropertyChanged;
        }

        if (newValue is not null)
        {
            newValue.PropertyChanged += OnSelectedTabPropertyChanged;
        }

        foreach (TerminalTabViewModel tab in Tabs)
        {
            tab.IsSelected = ReferenceEquals(tab, newValue);
        }

        if (newValue is not null)
        {
            StatusText = $"Active tab: {newValue.Title}";
            newValue.RequestFocus();
        }

        OnPropertyChanged(nameof(Title));
        CloseSelectedTabCommand.NotifyCanExecuteChanged();
        NextTabCommand.NotifyCanExecuteChanged();
        PreviousTabCommand.NotifyCanExecuteChanged();
    }

    private void OnSelectedTabPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(TerminalTabViewModel.Title))
        {
            OnPropertyChanged(nameof(Title));
        }
    }

    partial void OnHasOpenTabsChanged(bool value) =>
        NotifyTabVisibilityChanged();

    partial void OnIsEditorOpenChanged(bool value) => NotifyTabVisibilityChanged();

    partial void OnIsDeleteConfirmationOpenChanged(bool value) => NotifyTabVisibilityChanged();

    partial void OnIsSettingsOpenChanged(bool value) => NotifyTabVisibilityChanged();

    partial void OnIsPowerShellSetupOpenChanged(bool value) =>
        NotifyTabVisibilityChanged();

    partial void OnSettingsDataStatusChanged(string? value) =>
        OnPropertyChanged(nameof(HasSettingsDataStatus));

    partial void OnIsShortcutsOpenChanged(bool value) => NotifyTabVisibilityChanged();

    partial void OnIsFolderEditorOpenChanged(bool value) => NotifyTabVisibilityChanged();

    partial void OnIsFolderDeleteConfirmationOpenChanged(bool value) =>
        NotifyTabVisibilityChanged();

    [RelayCommand(CanExecute = nameof(HasSelectedSession))]
    private async Task OpenSelectedSessionAsync()
    {
        SessionListItemViewModel session = SelectedSession!;
        TerminalTabViewModel? existingTab = Tabs.FirstOrDefault(
            tab => tab.SessionId == session.Id);

        if (existingTab is not null)
        {
            SelectedTab = existingTab;
            existingTab.RequestFocus();
            StatusText = $"Session ‘{session.Name}’ is already open";
            return;
        }

        try
        {
            Session entity = await sessionService.GetByIdAsync(session.Id)
                ?? throw new KeyNotFoundException($"Session ‘{session.Name}’ was not found.");
            TerminalSessionDefinition definition =
                await terminalSessionFactory.CreateAsync(entity);

            var tab = new TerminalTabViewModel(
                session,
                definition,
                ptySessionFactory,
                applicationSettings.TerminalFontFamily,
                applicationSettings.TerminalFontSize,
                applicationSettings.TerminalSelectionColor,
                applicationSettings.TerminalCursorStyle,
                applicationSettings.TerminalCursorBlink,
                CloseTabAsync);
            tab.ApplicationCommandRequested += OnApplicationCommandRequested;
            Tabs.Add(tab);
            SelectedTab = tab;
            HasOpenTabs = true;
            StatusText = $"Terminal prepared for ‘{session.Name}’";
        }
        catch (TerminalLaunchException exception)
        {
            StatusText = exception.Message;
            SettingsError = exception.Message;
            OpenSettings();
        }
        catch (KeyNotFoundException exception)
        {
            StatusText = exception.Message;
            await ReloadSessionsAsync(null);
        }
    }

    [RelayCommand]
    private async Task OpenLocalTerminalAsync()
    {
        try
        {
            TerminalSessionDefinition definition =
                await terminalSessionFactory.CreateLocalAsync();
            const string title = "PowerShell";
            var tab = new TerminalTabViewModel(
                title,
                definition,
                ptySessionFactory,
                applicationSettings.TerminalFontFamily,
                applicationSettings.TerminalFontSize,
                applicationSettings.TerminalSelectionColor,
                applicationSettings.TerminalCursorStyle,
                applicationSettings.TerminalCursorBlink,
                CloseTabAsync);

            tab.ApplicationCommandRequested += OnApplicationCommandRequested;
            Tabs.Add(tab);
            SelectedTab = tab;
            HasOpenTabs = true;
            StatusText = $"Local terminal ‘{title}’ opened";
        }
        catch (TerminalLaunchException exception)
        {
            StatusText = exception.Message;
            SettingsError = exception.Message;
            OpenSettings();
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelectedTab))]
    private Task CloseSelectedTabAsync() => CloseTabAsync(SelectedTab!);

    [RelayCommand]
    private void RequestCloseWindow() =>
        CloseWindowRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand(CanExecute = nameof(HasSelectedTab))]
    private void NextTab() => SelectRelativeTab(1);

    [RelayCommand(CanExecute = nameof(HasSelectedTab))]
    private void PreviousTab() => SelectRelativeTab(-1);

    private void SelectRelativeTab(int offset)
    {
        if (Tabs.Count == 0)
        {
            return;
        }

        int currentIndex = SelectedTab is null ? -1 : Tabs.IndexOf(SelectedTab);
        int nextIndex = currentIndex < 0
            ? offset > 0 ? 0 : Tabs.Count - 1
            : (currentIndex + offset + Tabs.Count) % Tabs.Count;

        SelectedTab = Tabs[nextIndex];
        SelectedTab.RequestFocus();
    }

    [RelayCommand]
    private void OpenSettings()
    {
        if (IsPowerShellSetupOpen)
        {
            return;
        }

        SettingsPowerShellPath = applicationSettings.PowerShellPath;
        SettingsTheme = NormalizeTheme(applicationSettings.Theme);
        SettingsTerminalFontFamily = applicationSettings.TerminalFontFamily;
        SettingsTerminalFontSize = (decimal)applicationSettings.TerminalFontSize;
        SettingsTerminalSelectionColor = FindSelectionColorOption(
            applicationSettings.TerminalSelectionColor);
        SettingsTerminalCursorStyle = NormalizeCursorStyle(
            applicationSettings.TerminalCursorStyle);
        SettingsTerminalCursorBlink = applicationSettings.TerminalCursorBlink;
        LoadSystemFonts();
        SettingsError = null;
        SettingsDataStatus = null;
        IsSettingsOpen = true;
    }

    [RelayCommand]
    private void ToggleStatusBar() =>
        IsStatusBarVisible = !IsStatusBarVisible;

    [RelayCommand]
    private void ToggleSidebar() =>
        IsSidebarVisible = !IsSidebarVisible;

    [RelayCommand]
    private void ToggleRootFolderSort()
    {
        rootFoldersDescending = !rootFoldersDescending;
        OnPropertyChanged(nameof(RootFolderSortGlyph));
        OnPropertyChanged(nameof(RootFolderSortTooltip));
        ApplyFilter(SelectedSession?.Id);
    }

    [RelayCommand]
    private void OpenShortcuts() =>
        IsShortcutsOpen = true;

    [RelayCommand]
    private void CloseShortcuts() =>
        IsShortcutsOpen = false;

    [RelayCommand]
    private void CloseActiveOverlay()
    {
        if (IsPowerShellSetupOpen)
        {
            return;
        }
        else if (IsFolderDeleteConfirmationOpen)
        {
            CancelDeleteFolder();
        }
        else if (IsDeleteConfirmationOpen)
        {
            CancelDeleteSession();
        }
        else if (IsFolderEditorOpen)
        {
            CancelFolderEditor();
        }
        else if (IsEditorOpen)
        {
            CancelEditor();
        }
        else if (IsSettingsOpen)
        {
            CancelSettings();
        }
        else if (IsShortcutsOpen)
        {
            CloseShortcuts();
        }
    }

    [RelayCommand]
    private void OpenFolderEditor()
    {
        PrepareFolderEditor(string.Empty);
    }

    [RelayCommand(CanExecute = nameof(HasSelectedFolder))]
    private void OpenSubfolderEditor()
    {
        PrepareFolderEditor($"{SelectedTreeNode!.Path}/");
    }

    private void PrepareFolderEditor(string initialPath)
    {
        editingFolderPath = null;
        FolderEditorTitle = "New folder";
        FolderEditorAction = "Create";
        FolderPath = initialPath;
        FolderError = null;
        IsFolderEditorOpen = true;
    }

    [RelayCommand]
    private void ContextNewFolder(SessionTreeNodeViewModel? node)
    {
        if (node?.IsFolder == true)
        {
            PrepareFolderEditor($"{node.Path}/");
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelectedFolder))]
    private void EditFolder()
    {
        editingFolderPath = SelectedTreeNode!.Path;
        FolderEditorTitle = "Edit folder";
        FolderEditorAction = "Save";
        FolderPath = editingFolderPath;
        FolderError = null;
        IsFolderEditorOpen = true;
    }

    [RelayCommand]
    private void ContextEditFolder(SessionTreeNodeViewModel? node)
    {
        if (node?.IsFolder != true)
        {
            return;
        }

        editingFolderPath = node.Path;
        FolderEditorTitle = "Edit folder";
        FolderEditorAction = "Save";
        FolderPath = editingFolderPath;
        FolderError = null;
        IsFolderEditorOpen = true;
    }

    [RelayCommand]
    private void CancelFolderEditor()
    {
        IsFolderEditorOpen = false;
        FolderError = null;
    }

    [RelayCommand]
    private async Task CreateFolderAsync()
    {
        try
        {
            string resultingPath;
            if (editingFolderPath is null)
            {
                SessionFolder folder = await sessionFolderService.CreateAsync(FolderPath);
                resultingPath = folder.Path;
                StatusText = $"Folder ‘{resultingPath}’ created";
            }
            else
            {
                await sessionFolderService.RenameAsync(editingFolderPath, FolderPath);
                resultingPath = FolderPath.Trim().Replace('\\', '/').Trim('/');
                StatusText = $"Folder renamed to ‘{resultingPath}’";
            }

            IsFolderEditorOpen = false;
            FolderError = null;
            editingFolderPath = null;
            await ReloadSessionsAsync(null);
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException or KeyNotFoundException)
        {
            FolderError = exception.Message;
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelectedFolder))]
    private void RequestDeleteFolder()
    {
        PrepareFolderDeletion(selectedFolderPaths.Count > 0
            ? selectedFolderPaths.ToArray()
            : [SelectedTreeNode!.Path]);
    }

    [RelayCommand]
    private void RequestDeleteSelectedFolders()
    {
        if (selectedFolderPaths.Count > 1)
        {
            PrepareFolderDeletion(selectedFolderPaths.ToArray());
        }
    }

    [RelayCommand]
    private void ContextDeleteFolder(SessionTreeNodeViewModel? node)
    {
        if (node?.IsFolder != true)
        {
            return;
        }

        PrepareFolderDeletion(node.IsSelectedForDeletion && selectedFolderPaths.Count > 0
            ? selectedFolderPaths.ToArray()
            : [node.Path]);
    }

    private void PrepareFolderDeletion(string[] paths)
    {
        foldersPendingDeletion = paths;
        FolderDeleteTitle = foldersPendingDeletion.Length == 1
            ? "Delete folder?"
            : $"Delete {foldersPendingDeletion.Length} folders?";
        FolderDeleteMessage = foldersPendingDeletion.Length == 1
            ? $"Folder ‘{foldersPendingDeletion[0]}’ and its subfolders will be deleted."
            : $"{foldersPendingDeletion.Length} selected folders and their subfolders will be deleted.";
        ForceDeleteFolders = false;
        FolderDeleteError = null;
        IsFolderDeleteConfirmationOpen = true;
    }

    [RelayCommand]
    private void CancelDeleteFolder()
    {
        IsFolderDeleteConfirmationOpen = false;
        FolderDeleteError = null;
        ForceDeleteFolders = false;
        foldersPendingDeletion = [];
    }

    [RelayCommand]
    private async Task ConfirmDeleteFolderAsync()
    {
        try
        {
            FolderDeleteResult result = await sessionFolderService.DeleteAsync(
                foldersPendingDeletion,
                ForceDeleteFolders);
            IsFolderDeleteConfirmationOpen = false;
            FolderDeleteError = null;
            ForceDeleteFolders = false;
            foldersPendingDeletion = [];
            StatusText = result.DeletedSessions == 0
                ? $"Deleted {result.DeletedFolders} folder(s)"
                : $"Deleted {result.DeletedFolders} folder(s) and {result.DeletedSessions} session(s)";
            await ReloadSessionsAsync(null);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or KeyNotFoundException)
        {
            FolderDeleteError = exception.Message;
        }
    }

    [RelayCommand]
    private void CancelSettings()
    {
        IsSettingsOpen = false;
        SettingsError = null;
    }

    [RelayCommand]
    private async Task SelectPowerShellAsync()
    {
        string? selectedPath = await PickPowerShellAsync();
        if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            SettingsPowerShellPath = selectedPath;
            SettingsError = null;
        }
    }

    [RelayCommand]
    private async Task UseDefaultPowerShellAsync()
    {
        SettingsPowerShellPath = "pwsh.exe";
        await CompletePowerShellSetupAsync();
    }

    [RelayCommand]
    private async Task ChooseInitialPowerShellAsync()
    {
        PowerShellSetupError = null;
        string? selectedPath = await PickPowerShellAsync();
        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            PowerShellSetupError = SettingsError;
            return;
        }

        SettingsPowerShellPath = selectedPath;
        await CompletePowerShellSetupAsync();
    }

    private async Task CompletePowerShellSetupAsync()
    {
        PowerShellSetupError = null;
        if (!await TrySaveSettingsAsync())
        {
            PowerShellSetupError = SettingsError;
            return;
        }

        requiresInitialPowerShellSelection = false;
        IsPowerShellSetupOpen = false;
        await OpenLocalTerminalAsync();
    }

    private async Task<string?> PickPowerShellAsync()
    {
        try
        {
            return await executableFilePicker.PickPowerShellAsync();
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or IOException)
        {
            SettingsError = $"Could not select the executable: {exception.Message}";
            return null;
        }
    }

    [RelayCommand]
    private async Task ExportSessionsAsync()
    {
        try
        {
            Stream? destination = await sessionArchiveFilePicker.OpenExportStreamAsync();
            if (destination is null)
            {
                return;
            }

            await using (destination)
            {
                await sessionArchiveService.ExportAsync(destination);
            }

            SettingsError = null;
            SettingsDataStatus = "Sessions and folders exported successfully.";
            StatusText = "Session archive exported";
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            SettingsDataStatus = null;
            SettingsError = $"Export failed: {exception.Message}";
        }
    }

    [RelayCommand]
    private async Task ImportSessionsAsync()
    {
        try
        {
            Stream? source = await sessionArchiveFilePicker.OpenImportStreamAsync();
            if (source is null)
            {
                return;
            }

            SessionImportResult result;
            await using (source)
            {
                result = await sessionArchiveService.ImportAsync(source);
            }

            await ReloadSessionsAsync(SelectedSession?.Id);
            SettingsError = null;
            SettingsDataStatus =
                $"Imported {result.ImportedSessions} sessions " +
                $"({result.AddedSessions} new, {result.UpdatedSessions} updated) " +
                $"and {result.AddedFolders} folders.";
            StatusText = "Session archive imported";
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            SettingsDataStatus = null;
            SettingsError = $"Import failed: {exception.Message}";
        }
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        if (!await TrySaveSettingsAsync() || !requiresInitialPowerShellSelection)
        {
            return;
        }

        requiresInitialPowerShellSelection = false;
        await OpenLocalTerminalAsync();
    }

    private async Task<bool> TrySaveSettingsAsync()
    {
        string powerShellPath = SettingsPowerShellPath.Trim().Trim('"');
        if (powerShellPath.Length == 0)
        {
            SettingsError = "Select pwsh.exe or powershell.exe.";
            return false;
        }

        if (Path.IsPathRooted(powerShellPath) && !File.Exists(powerShellPath))
        {
            SettingsError = "The selected file does not exist.";
            return false;
        }

        string executableName = Path.GetFileName(powerShellPath);
        if (!executableName.Equals("pwsh.exe", StringComparison.OrdinalIgnoreCase) &&
            !executableName.Equals("powershell.exe", StringComparison.OrdinalIgnoreCase))
        {
            SettingsError = "Select a PowerShell executable named pwsh.exe or powershell.exe.";
            return false;
        }

        try
        {
            string theme = NormalizeTheme(SettingsTheme);
            string fontFamily = string.IsNullOrWhiteSpace(SettingsTerminalFontFamily)
                ? "Cascadia Mono"
                : SettingsTerminalFontFamily.Trim();
            double fontSize = Math.Clamp((double)SettingsTerminalFontSize, 8, 32);
            TerminalSelectionColorOption selectionColor =
                FindSelectionColorOption(SettingsTerminalSelectionColor.Value);
            string cursorStyle = NormalizeCursorStyle(SettingsTerminalCursorStyle);
            applicationSettings = applicationSettings with
            {
                PowerShellPath = powerShellPath,
                Theme = theme,
                TerminalFontFamily = fontFamily,
                TerminalFontSize = fontSize,
                TerminalSelectionColor = selectionColor.Value,
                TerminalCursorStyle = cursorStyle,
                TerminalCursorBlink = SettingsTerminalCursorBlink,
            };
            await settingsService.SaveAsync(applicationSettings);
            SettingsPowerShellPath = powerShellPath;
            SettingsTheme = theme;
            SettingsTerminalFontFamily = fontFamily;
            SettingsTerminalFontSize = (decimal)fontSize;
            SettingsTerminalSelectionColor = selectionColor;
            SettingsTerminalCursorStyle = cursorStyle;
            foreach (TerminalTabViewModel tab in Tabs)
            {
                tab.UpdateAppearance(
                    fontFamily,
                    fontSize,
                    selectionColor.Value,
                    cursorStyle,
                    SettingsTerminalCursorBlink);
            }
            themeService.Apply(theme);
            SettingsError = null;
            IsSettingsOpen = false;
            UpdateTerminalStatus();
            StatusText = "Settings saved";
            return true;
        }
        catch (IOException exception)
        {
            SettingsError = $"Failed to save settings: {exception.Message}";
            return false;
        }
    }

    [RelayCommand]
    private void NewSession() =>
        PrepareNewSession(string.Empty);

    [RelayCommand(CanExecute = nameof(HasSelectedFolder))]
    private void NewSessionInSelectedFolder() =>
        PrepareNewSession(SelectedTreeNode!.Path);

    [RelayCommand]
    private void ContextNewSession(SessionTreeNodeViewModel? node)
    {
        if (node?.IsFolder == true)
        {
            PrepareNewSession(node.Path);
        }
    }

    private void PrepareNewSession(string folder)
    {
        editingSessionId = null;
        EditorTitle = "New session";
        EditorName = string.Empty;
        EditorHost = string.Empty;
        EditorPort = 22;
        EditorUsername = string.Empty;
        EditorPrivateKey = string.Empty;
        EditorFolder = folder;
        EditorNotes = string.Empty;
        EditorError = null;
        IsEditorOpen = true;
    }

    [RelayCommand(CanExecute = nameof(HasSelectedSession))]
    private void EditSession()
    {
        PrepareEditSession(SelectedSession!);
    }

    [RelayCommand]
    private void ContextEditSession(SessionTreeNodeViewModel? node)
    {
        if (node?.Session is not null)
        {
            PrepareEditSession(node.Session);
        }
    }

    private void PrepareEditSession(SessionListItemViewModel session)
    {

        editingSessionId = session.Id;
        EditorTitle = "Edit session";
        EditorName = session.Name;
        EditorHost = session.Host;
        EditorPort = session.Port;
        EditorUsername = session.Username;
        EditorPrivateKey = session.PrivateKey ?? string.Empty;
        EditorFolder = session.Folder;
        EditorNotes = session.Notes ?? string.Empty;
        EditorError = null;
        IsEditorOpen = true;
    }

    [RelayCommand]
    private void CancelEditor()
    {
        IsEditorOpen = false;
        EditorError = null;
    }

    [RelayCommand]
    private async Task SaveSessionAsync()
    {
        EditorError = null;

        try
        {
            var details = new SessionDetails(
                EditorName,
                EditorHost,
                decimal.ToInt32(EditorPort),
                EditorUsername,
                EditorPrivateKey,
                EditorFolder,
                EditorNotes);

            Session session = editingSessionId is Guid id
                ? await sessionService.UpdateAsync(id, details)
                : await sessionService.CreateAsync(details);

            IsEditorOpen = false;
            StatusText = editingSessionId.HasValue ? "Session updated" : "Session created";
            await ReloadSessionsAsync(session.Id);
        }
        catch (Exception exception) when (
            exception is ArgumentException or OverflowException or KeyNotFoundException)
        {
            EditorError = exception.Message;
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelectedSession))]
    private void RequestDeleteSession()
    {
        sessionPendingDeletion = SelectedSession;
        IsDeleteConfirmationOpen = true;
    }

    [RelayCommand]
    private void ContextDeleteSession(SessionTreeNodeViewModel? node)
    {
        if (node?.Session is null)
        {
            return;
        }

        sessionPendingDeletion = node.Session;
        IsDeleteConfirmationOpen = true;
    }

    [RelayCommand]
    private void CancelDeleteSession()
    {
        IsDeleteConfirmationOpen = false;
        sessionPendingDeletion = null;
    }

    [RelayCommand]
    private async Task ConfirmDeleteSessionAsync()
    {
        SessionListItemViewModel? session = sessionPendingDeletion ?? SelectedSession;
        if (session is null)
        {
            IsDeleteConfirmationOpen = false;
            return;
        }

        Guid id = session.Id;
        string name = session.Name;

        try
        {
            await sessionService.DeleteAsync(id);
            IsDeleteConfirmationOpen = false;
            sessionPendingDeletion = null;
            StatusText = $"Session ‘{name}’ deleted";
            await ReloadSessionsAsync(null);
        }
        catch (KeyNotFoundException)
        {
            IsDeleteConfirmationOpen = false;
            sessionPendingDeletion = null;
            StatusText = "Session not found";
            await ReloadSessionsAsync(null);
        }
    }

    private bool HasSelectedSession() => SelectedSession is not null;

    private bool HasSelectedFolder() => SelectedTreeNode?.IsFolder == true;

    private bool HasSelectedTab() => SelectedTab is not null;

    public async Task MoveSessionAsync(Guid sessionId, string destinationFolder)
    {
        try
        {
            Session session = await sessionService.MoveAsync(sessionId, destinationFolder);
            StatusText = string.IsNullOrWhiteSpace(session.Folder)
                ? $"Session ‘{session.Name}’ moved to root"
                : $"Session ‘{session.Name}’ moved to ‘{session.Folder}’";
            await ReloadSessionsAsync(session.Id);
        }
        catch (KeyNotFoundException exception)
        {
            StatusText = exception.Message;
            await ReloadSessionsAsync(null);
        }
    }

    private async Task ReloadSessionsAsync(
        Guid? sessionToSelect,
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
        await SynchronizeTabsAsync();
        ApplyFilter(sessionToSelect);

        if (sessions.Count == 0)
        {
            StatusText = "No sessions available";
        }
    }

    private void ApplyFilter(Guid? sessionToSelect)
    {
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
            AddSessionToTree(session, foldersByPath);
            visibleSessionCount++;
        }

        SortNodes(SessionTree, rootFoldersDescending);
        SessionCountText = visibleSessionCount == 1
            ? "1 session"
            : $"{visibleSessionCount} sessions";

        SelectedTreeNode = sessionToSelect.HasValue
            ? FindSessionNode(SessionTree, sessionToSelect.Value)
            : null;
    }

    private void RefreshFolderOptions()
    {
        string[] paths = allFolders
            .Select(folder => folder.Path)
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

    private void AddSessionToTree(
        SessionListItemViewModel session,
        IDictionary<string, SessionTreeNodeViewModel> foldersByPath)
    {
        ObservableCollection<SessionTreeNodeViewModel> parentNodes =
            EnsureFolderPath(session.Folder, foldersByPath);

        parentNodes.Add(SessionTreeNodeViewModel.CreateSession(session));
    }

    private ObservableCollection<SessionTreeNodeViewModel> EnsureFolderPath(
        string folderPath,
        IDictionary<string, SessionTreeNodeViewModel> foldersByPath)
    {
        string normalizedFolder = folderPath.Replace('\\', '/');
        string[] segments = normalizedFolder.Split(
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
            ? folders.OrderByDescending(
                node => node.Name,
                StringComparer.CurrentCultureIgnoreCase)
            : folders.OrderBy(
                node => node.Name,
                StringComparer.CurrentCultureIgnoreCase);
        SessionTreeNodeViewModel[] sortedNodes = folders
            .Concat(nodes
                .Where(node => node.IsSession)
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

    private async Task CloseTabAsync(TerminalTabViewModel tab)
    {
        int closedTabIndex = Tabs.IndexOf(tab);
        if (closedTabIndex < 0)
        {
            return;
        }

        try
        {
            await tab.TerminateAsync();
            await tab.DisposeAsync();
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
        {
            StatusText = $"Failed to stop PowerShell: {exception.Message}";
            return;
        }

        tab.ApplicationCommandRequested -= OnApplicationCommandRequested;
        bool wasSelected = ReferenceEquals(SelectedTab, tab);
        Tabs.RemoveAt(closedTabIndex);

        if (wasSelected)
        {
            int nextTabIndex = Math.Min(closedTabIndex, Tabs.Count - 1);
            SelectedTab = nextTabIndex >= 0 ? Tabs[nextTabIndex] : null;
        }

        HasOpenTabs = Tabs.Count > 0;
        StatusText = $"Tab ‘{tab.Title}’ closed";
    }

    private async void OnApplicationCommandRequested(object? sender, string command)
    {
        switch (command)
        {
            case "newSession":
                NewSession();
                break;
            case "openSession" when HasSelectedSession():
                await OpenSelectedSessionAsync();
                break;
            case "closeTab" when sender is TerminalTabViewModel tab:
                await CloseTabAsync(tab);
                break;
            case "nextTab":
                NextTab();
                break;
            case "previousTab":
                PreviousTab();
                break;
            case "closeWindow":
                RequestCloseWindow();
                break;
            case "toggleSidebar":
                ToggleSidebar();
                break;
            case "settings":
                OpenSettings();
                break;
        }
    }

    private async Task SynchronizeTabsAsync()
    {
        foreach (TerminalTabViewModel tab in Tabs.ToArray())
        {
            if (tab.IsLocal)
            {
                continue;
            }

            SessionListItemViewModel? session = allSessions.FirstOrDefault(
                item => item.Id == tab.SessionId);

            if (session is null)
            {
                await CloseTabAsync(tab);
            }
            else
            {
                tab.UpdateSession(session);
            }
        }
    }

    private void UpdateTerminalStatus()
    {
        TerminalStatusText = $"PowerShell: {Path.GetFileName(applicationSettings.PowerShellPath)}";
    }

    private void NotifyTabVisibilityChanged()
    {
        OnPropertyChanged(nameof(IsTabAreaEmpty));
        OnPropertyChanged(nameof(AreTerminalHostsVisible));
    }

    private static string NormalizeTheme(string? theme) => "Dark";

    private void LoadSystemFonts()
    {
        if (SystemFontFamilies.Count > 0)
        {
            return;
        }

        foreach (string fontFamily in systemFontService.GetInstalledFontFamilies())
        {
            SystemFontFamilies.Add(fontFamily);
        }

        if (!SystemFontFamilies.Contains(SettingsTerminalFontFamily))
        {
            SystemFontFamilies.Insert(0, SettingsTerminalFontFamily);
        }
    }

    private static string NormalizeCursorStyle(string? cursorStyle) =>
        cursorStyle?.Trim().ToLowerInvariant() switch
        {
            "block" => "Block",
            "underline" => "Underline",
            _ => "Bar",
        };

    private TerminalSelectionColorOption FindSelectionColorOption(string? value) =>
        TerminalSelectionColors.FirstOrDefault(option =>
            option.Value.Equals(value, StringComparison.OrdinalIgnoreCase))
        ?? TerminalSelectionColors[0];
}
