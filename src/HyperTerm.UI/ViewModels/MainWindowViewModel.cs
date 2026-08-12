using System.ComponentModel;
using Avalonia.Controls.Primitives;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HyperTerm.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HyperTerm.UI.ViewModels;

public sealed partial class MainWindowViewModel : ViewModelBase
{
    public MainWindowViewModel(
        SessionExplorerViewModel explorer,
        TerminalWorkspaceViewModel workspace,
        SettingsViewModel settings,
        SessionEditorViewModel sessionEditor,
        FolderEditorViewModel folderEditor,
        ILogger<MainWindowViewModel>? logger = null)
    {
        Explorer = explorer;
        Workspace = workspace;
        Settings = settings;
        SessionEditor = sessionEditor;
        FolderEditor = folderEditor;
        diagnostics = logger ?? NullLogger<MainWindowViewModel>.Instance;
        WireEvents();
    }

    private readonly ILogger<MainWindowViewModel> diagnostics;

    public event EventHandler? CloseWindowRequested;
    public event EventHandler? InitializationCompleted;
    public event EventHandler? TerminalSearchRequested;

    public SessionExplorerViewModel Explorer { get; }
    public TerminalWorkspaceViewModel Workspace { get; }
    public SettingsViewModel Settings { get; }
    public SessionEditorViewModel SessionEditor { get; }
    public FolderEditorViewModel FolderEditor { get; }

    public string Title => Workspace.Title;
    public WindowSettings WindowSettings => Settings.WindowSettings;
    public bool IsTabAreaEmpty => !Workspace.HasOpenTabs;
    public bool IsOverlayOpen =>
        Workspace.IsPsmuxCreateOpen ||
        Workspace.IsPsmuxSessionsOpen ||
        Workspace.IsPsmuxKillConfirmationOpen ||
        SessionEditor.IsEditorOpen ||
        SessionEditor.IsDeleteConfirmationOpen ||
        Settings.IsSettingsOpen ||
        FolderEditor.IsFolderEditorOpen ||
        FolderEditor.IsFolderDeleteConfirmationOpen ||
        IsShortcutsOpen ||
        IsCommandPaletteOpen;
    public ScrollBarVisibility SidebarScrollBarVisibility =>
        showSidebarScrollbar ? ScrollBarVisibility.Auto : ScrollBarVisibility.Hidden;
    public bool AreTerminalHostsVisible =>
        Workspace.HasOpenTabs &&
        !Workspace.IsPsmuxCreateOpen &&
        !Workspace.IsPsmuxSessionsOpen &&
        !SessionEditor.IsEditorOpen &&
        !SessionEditor.IsDeleteConfirmationOpen &&
        !Settings.IsSettingsOpen &&
        !FolderEditor.IsFolderEditorOpen &&
        !FolderEditor.IsFolderDeleteConfirmationOpen &&
        !IsShortcutsOpen &&
        !IsCommandPaletteOpen;

    [ObservableProperty]
    private bool isStatusBarVisible;

    [ObservableProperty]
    private bool isSidebarVisible = true;

    private bool showSidebarScrollbar;

    [ObservableProperty]
    private bool isShortcutsOpen;

    [ObservableProperty]
    private bool isInitializing = true;

    public bool IsInitialized { get; private set; }

    partial void OnIsShortcutsOpenChanged(bool value) =>
        NotifyTerminalVisibilityChanged();

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await InitializeSettingsAsync(cancellationToken);
        await InitializeWorkspaceAsync(cancellationToken);
        CompleteInitialization();
    }

    internal async Task InitializeSettingsAsync(CancellationToken cancellationToken = default)
    {
        await Settings.InitializeAsync(cancellationToken);
        ApplySidebarScrollbarSetting(Settings.Current);
        Workspace.ApplySettings(Settings.Current);
    }

    internal async Task InitializeWorkspaceAsync(CancellationToken cancellationToken = default)
    {
        await Explorer.InitializeAsync(cancellationToken);
        await Workspace.RefreshPsmuxSessionsAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await Workspace.OpenLocalTerminalCommand.ExecuteAsync(null);
    }

    internal void CompleteInitialization()
    {
        IsInitialized = true;
        IsInitializing = false;
        InitializationCompleted?.Invoke(this, EventArgs.Empty);
    }

    internal void ReportStartupFailure(Exception exception)
    {
        IsInitializing = false;
        Workspace.SetStatus($"Startup failed: {exception.Message}");
        Settings.OpenWithError($"HyperTerm could not finish starting: {exception.Message}");
        InitializationCompleted?.Invoke(this, EventArgs.Empty);
    }

    public async Task ShutdownAsync()
    {
        await Workspace.ShutdownAsync();
        await Settings.ShutdownAsync();
    }

    public void CaptureWindowState(double width, double height, int x, int y) =>
        CaptureWindowStateIfInitialized(width, height, x, y);

    private void CaptureWindowStateIfInitialized(double width, double height, int x, int y)
    {
        if (IsInitialized)
        {
            Settings.CaptureWindowState(width, height, x, y);
        }
    }

    [RelayCommand]
    private void RequestCloseWindow() =>
        CloseWindowRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void ToggleStatusBar() => IsStatusBarVisible = !IsStatusBarVisible;

    [RelayCommand]
    private void ToggleSidebar() => IsSidebarVisible = !IsSidebarVisible;

    [RelayCommand]
    private void OpenShortcuts() => IsShortcutsOpen = true;

    [RelayCommand]
    private void CloseShortcuts() => IsShortcutsOpen = false;

    [RelayCommand(CanExecute = nameof(CanSearchTerminal))]
    private void OpenTerminalSearch() =>
        TerminalSearchRequested?.Invoke(this, EventArgs.Empty);

    private bool CanSearchTerminal() => Workspace.SelectedTab is not null;

    [RelayCommand]
    private void CloseActiveOverlay()
    {
        if (FolderEditor.IsFolderDeleteConfirmationOpen)
        {
            FolderEditor.CancelDeleteFolder();
        }
        else if (IsCommandPaletteOpen)
        {
            CloseCommandPalette();
        }
        else if (SessionEditor.IsDeleteConfirmationOpen)
        {
            SessionEditor.CancelDeleteSession();
        }
        else if (FolderEditor.IsFolderEditorOpen)
        {
            FolderEditor.CancelFolderEditor();
        }
        else if (SessionEditor.IsEditorOpen)
        {
            SessionEditor.CancelEditor();
        }
        else if (Settings.IsSettingsOpen)
        {
            Settings.CancelSettings();
        }
        else if (IsShortcutsOpen)
        {
            IsShortcutsOpen = false;
        }
        else if (Workspace.IsPsmuxCreateOpen)
        {
            Workspace.CancelPsmuxCreateCommand.Execute(null);
        }
        else if (Workspace.IsPsmuxKillConfirmationOpen)
        {
            Workspace.CancelKillPsmuxSessionCommand.Execute(null);
        }
        else if (Workspace.IsPsmuxSessionsOpen)
        {
            Workspace.ClosePsmuxSessionsCommand.Execute(null);
        }
    }

    private void WireEvents()
    {
        Explorer.SessionOpenRequested += OnSessionOpenRequested;
        Explorer.NewSessionRequested += SessionEditor.OpenNew;
        Explorer.EditSessionRequested += SessionEditor.OpenEdit;
        Explorer.DeleteSessionRequested += SessionEditor.RequestDelete;
        Explorer.NewFolderRequested += FolderEditor.OpenNew;
        Explorer.EditFolderRequested += FolderEditor.OpenEdit;
        Explorer.DeleteFoldersRequested += FolderEditor.OpenDelete;
        Explorer.SessionsReloaded += OnSessionsReloaded;
        Explorer.StatusRequested += Workspace.SetStatus;

        SessionEditor.SessionsChanged += OnSessionsChanged;
        SessionEditor.StatusRequested += Workspace.SetStatus;
        FolderEditor.FoldersChanged += OnFoldersChanged;
        FolderEditor.StatusRequested += Workspace.SetStatus;

        Settings.SettingsSaved += OnSettingsSaved;
        Settings.SessionsImported += OnSessionsImported;
        Settings.StatusRequested += Workspace.SetStatus;

        Workspace.SettingsRequested += Settings.OpenWithError;
        Workspace.SessionsRefreshRequested += OnSessionsRefreshRequested;
        Workspace.ApplicationCommandRequested += OnApplicationCommandRequested;

        Workspace.PropertyChanged += OnChildPropertyChanged;
        Settings.PropertyChanged += OnChildPropertyChanged;
        SessionEditor.PropertyChanged += OnChildPropertyChanged;
        FolderEditor.PropertyChanged += OnChildPropertyChanged;
    }

    private void OnSessionOpenRequested(SessionListItemViewModel session) =>
        Observe(Workspace.OpenSessionAsync(session), "open terminal session");

    private void OnSessionsReloaded(IReadOnlyList<SessionListItemViewModel> sessions) =>
        Observe(Workspace.SynchronizeTabsAsync(sessions), "synchronize terminal tabs");

    private void OnSessionsChanged(Guid? sessionId) =>
        Observe(Explorer.ReloadAsync(sessionId), "reload sessions");

    private void OnFoldersChanged() =>
        Observe(Explorer.ReloadAsync(), "reload folders");

    private void OnSessionsImported() =>
        Observe(
            Explorer.ReloadAsync(Explorer.SelectedSession?.Id),
            "reload imported sessions");

    private void OnSettingsSaved(ApplicationSettings settings)
    {
        Workspace.ApplySettings(settings);
        ApplySidebarScrollbarSetting(settings);
    }

    private void ApplySidebarScrollbarSetting(ApplicationSettings settings)
    {
        showSidebarScrollbar = settings.ShowSidebarScrollbar;
        OnPropertyChanged(nameof(SidebarScrollBarVisibility));
    }

    private void OnSessionsRefreshRequested() =>
        Observe(Explorer.ReloadAsync(), "refresh sessions");

    private void OnApplicationCommandRequested(string command) =>
        Observe(HandleApplicationCommandAsync(command), "execute application command");

    private async Task HandleApplicationCommandAsync(string command)
    {
        switch (command)
        {
            case "newSession":
                SessionEditor.OpenNew(string.Empty);
                break;
            case "openSession" when Explorer.SelectedSession is not null:
                await Workspace.OpenSessionAsync(Explorer.SelectedSession);
                break;
            case "closeWindow":
                CloseWindowRequested?.Invoke(this, EventArgs.Empty);
                break;
            case "toggleSidebar":
                IsSidebarVisible = !IsSidebarVisible;
                break;
            case "settings":
                Settings.OpenSettingsCommand.Execute(null);
                break;
            case "searchTerminal":
                OpenTerminalSearch();
                break;
            case "commandPalette":
                OpenCommandPalette();
                break;
        }
    }

    private void Observe(Task operation, string operationName) =>
        _ = ObserveAsync(operation, operationName);

    private async Task ObserveAsync(Task operation, string operationName)
    {
        try
        {
            await operation;
        }
        catch (Exception exception)
        {
            diagnostics.LogError(
                exception,
                "Failed to {Operation}.",
                operationName);
            Workspace.SetStatus($"Failed to {operationName}: {exception.Message}");
        }
    }

    private void OnChildPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (ReferenceEquals(sender, Workspace) &&
            eventArgs.PropertyName == nameof(TerminalWorkspaceViewModel.Title))
        {
            OnPropertyChanged(nameof(Title));
        }

        if (ReferenceEquals(sender, Workspace) &&
            eventArgs.PropertyName == nameof(TerminalWorkspaceViewModel.SelectedTab))
        {
            OpenTerminalSearchCommand.NotifyCanExecuteChanged();
        }

        bool terminalVisibilityChanged =
            ReferenceEquals(sender, Workspace) &&
            eventArgs.PropertyName == nameof(TerminalWorkspaceViewModel.HasOpenTabs) ||
            ReferenceEquals(sender, Workspace) &&
            eventArgs.PropertyName is nameof(TerminalWorkspaceViewModel.IsPsmuxCreateOpen) or
                nameof(TerminalWorkspaceViewModel.IsPsmuxSessionsOpen) or
                nameof(TerminalWorkspaceViewModel.IsPsmuxKillConfirmationOpen) ||
            ReferenceEquals(sender, Settings) &&
            eventArgs.PropertyName == nameof(SettingsViewModel.IsSettingsOpen) ||
            ReferenceEquals(sender, SessionEditor) &&
            eventArgs.PropertyName is nameof(SessionEditorViewModel.IsEditorOpen) or
                nameof(SessionEditorViewModel.IsDeleteConfirmationOpen) ||
            ReferenceEquals(sender, FolderEditor) &&
            eventArgs.PropertyName is nameof(FolderEditorViewModel.IsFolderEditorOpen) or
                nameof(FolderEditorViewModel.IsFolderDeleteConfirmationOpen);
        if (terminalVisibilityChanged)
        {
            NotifyTerminalVisibilityChanged();
        }
    }

    private void NotifyTerminalVisibilityChanged()
    {
        OnPropertyChanged(nameof(IsTabAreaEmpty));
        OnPropertyChanged(nameof(IsOverlayOpen));
        OnPropertyChanged(nameof(AreTerminalHostsVisible));
    }
}
