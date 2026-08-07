using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HyperTerm.Core.Abstractions.Services;
using HyperTerm.Core.Abstractions.Terminal;
using HyperTerm.Core.Entities;
using HyperTerm.Core.Exceptions;
using HyperTerm.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HyperTerm.UI.ViewModels;

public sealed partial class TerminalWorkspaceViewModel(
    ISessionService sessionService,
    ITerminalSessionFactory terminalSessionFactory,
    IPtySessionFactory ptySessionFactory,
    IPsmuxService? psmuxService = null,
    ILogger<TerminalWorkspaceViewModel>? logger = null) : ViewModelBase
{
    private static readonly Regex PsmuxNamePattern = new(
        "^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$",
        RegexOptions.CultureInvariant);
    private ApplicationSettings settings = new();
    private readonly ILogger<TerminalWorkspaceViewModel> diagnostics =
        logger ?? NullLogger<TerminalWorkspaceViewModel>.Instance;

    public event Action<string>? ApplicationCommandRequested;

    public event Action<string>? SettingsRequested;

    public event Action? SessionsRefreshRequested;

    public ObservableCollection<TerminalTabViewModel> Tabs { get; } = [];

    public ObservableCollection<PsmuxSessionItemViewModel> PsmuxSessions { get; } = [];

    public bool HasPsmuxSessions => PsmuxSessions.Count > 0;

    public bool HasPsmuxSessionsMessage => !string.IsNullOrWhiteSpace(PsmuxSessionsMessage);

    public bool HasSelectedPsmuxSession => SelectedPsmuxSession is not null;

    public bool HasPsmuxKillError => !string.IsNullOrWhiteSpace(PsmuxKillError);

    public string Title => "HyperTerm";

    public bool HasOpenTabs => Tabs.Count > 0;

    [ObservableProperty]
    private TerminalTabViewModel? selectedTab;

    [ObservableProperty]
    private string statusText = "Ready";

    [ObservableProperty]
    private string terminalStatusText = "PowerShell";

    [ObservableProperty]
    private bool isPsmuxAvailable;

    [ObservableProperty]
    private bool isPsmuxSessionsOpen;

    [ObservableProperty]
    private bool isRefreshingPsmuxSessions;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPsmuxSessionsMessage))]
    private string? psmuxSessionsMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedPsmuxSession))]
    private PsmuxSessionItemViewModel? selectedPsmuxSession;

    [ObservableProperty]
    private bool isPsmuxKillConfirmationOpen;

    [ObservableProperty]
    private bool isKillingPsmuxSession;

    [ObservableProperty]
    private PsmuxSessionItemViewModel? psmuxSessionPendingKill;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPsmuxKillError))]
    private string? psmuxKillError;

    [ObservableProperty]
    private bool isPsmuxCreateOpen;

    [ObservableProperty]
    private string psmuxSessionName = string.Empty;

    [ObservableProperty]
    private string? psmuxError;

    [ObservableProperty]
    private bool isPsmuxDuplicate;

    partial void OnPsmuxSessionNameChanged(string value)
    {
        IsPsmuxDuplicate = false;
        PsmuxError = null;
    }

    partial void OnSelectedTabChanged(
        TerminalTabViewModel? oldValue,
        TerminalTabViewModel? newValue)
    {
        foreach (TerminalTabViewModel tab in Tabs)
        {
            tab.IsSelected = ReferenceEquals(tab, newValue);
        }

        if (newValue is not null)
        {
            StatusText = $"Active tab: {newValue.Title}";
            newValue.RequestFocus();
        }

        CloseSelectedTabCommand.NotifyCanExecuteChanged();
        NextTabCommand.NotifyCanExecuteChanged();
        PreviousTabCommand.NotifyCanExecuteChanged();
    }

    public void ApplySettings(ApplicationSettings value)
    {
        ArgumentNullException.ThrowIfNull(value);
        settings = value;
        TerminalStatusText = $"PowerShell: {Path.GetFileName(settings.PowerShellPath)}";

        foreach (TerminalTabViewModel tab in Tabs)
        {
            tab.UpdateAppearance(
                settings.TerminalFontFamily,
                settings.TerminalFontSize,
                settings.TerminalSelectionColor,
                settings.TerminalCursorStyle,
                settings.TerminalCursorBlink);
        }
    }

    public void SetStatus(string value) => StatusText = value;

    [RelayCommand]
    public async Task RefreshPsmuxSessionsAsync(
        CancellationToken cancellationToken = default)
    {
        string? selectedName = SelectedPsmuxSession?.Name;
        IsRefreshingPsmuxSessions = true;
        PsmuxSessionsMessage = "Loading psmux sessions...";
        if (psmuxService is null)
        {
            IsPsmuxAvailable = false;
            PsmuxSessions.Clear();
            SelectedPsmuxSession = null;
            PsmuxSessionsMessage = "psmux integration is unavailable.";
            NotifyPsmuxSessionsChanged();
            IsRefreshingPsmuxSessions = false;
            return;
        }

        try
        {
            PsmuxAvailability availability = await psmuxService.ProbeAsync(cancellationToken);
            IsPsmuxAvailable = availability.IsAvailable;
            if (!availability.IsAvailable)
            {
                PsmuxSessions.Clear();
                SelectedPsmuxSession = null;
                PsmuxSessionsMessage = availability.Error ?? "psmux is unavailable.";
                NotifyPsmuxSessionsChanged();
                return;
            }

            IReadOnlyList<PsmuxSessionInfo> sessions =
                await psmuxService.ListSessionsAsync(cancellationToken);
            PsmuxSessions.Clear();
            foreach (PsmuxSessionInfo session in sessions)
            {
                PsmuxSessions.Add(new PsmuxSessionItemViewModel(session));
            }
            SelectedPsmuxSession = selectedName is null
                ? PsmuxSessions.FirstOrDefault()
                : PsmuxSessions.FirstOrDefault(session => session.Name.Equals(
                      selectedName,
                      StringComparison.OrdinalIgnoreCase)) ?? PsmuxSessions.FirstOrDefault();
            PsmuxSessionsMessage = PsmuxSessions.Count == 0
                ? "No active psmux sessions."
                : null;
            NotifyPsmuxSessionsChanged();
        }
        catch (Exception exception) when (
            exception is TerminalLaunchException or InvalidOperationException)
        {
            diagnostics.LogError(exception, "Failed to refresh psmux sessions.");
            IsPsmuxAvailable = false;
            PsmuxSessionsMessage = exception.Message;
            StatusText = exception.Message;
        }
        finally
        {
            IsRefreshingPsmuxSessions = false;
        }
    }

    [RelayCommand]
    private async Task OpenPsmuxSessionsAsync()
    {
        IsPsmuxSessionsOpen = true;
        await RefreshPsmuxSessionsAsync();
    }

    [RelayCommand]
    private void ClosePsmuxSessions()
    {
        CancelKillPsmuxSession();
        IsPsmuxSessionsOpen = false;
    }

    [RelayCommand]
    private void RequestKillPsmuxSession(PsmuxSessionItemViewModel? session)
    {
        if (session is null || IsKillingPsmuxSession)
        {
            return;
        }

        SelectedPsmuxSession = session;
        PsmuxSessionPendingKill = session;
        PsmuxKillError = null;
        IsPsmuxKillConfirmationOpen = true;
    }

    [RelayCommand]
    private void CancelKillPsmuxSession()
    {
        if (IsKillingPsmuxSession)
        {
            return;
        }

        IsPsmuxKillConfirmationOpen = false;
        PsmuxSessionPendingKill = null;
        PsmuxKillError = null;
    }

    [RelayCommand]
    private async Task ConfirmKillPsmuxSessionAsync()
    {
        PsmuxSessionItemViewModel? session = PsmuxSessionPendingKill;
        if (session is null || psmuxService is null || IsKillingPsmuxSession)
        {
            return;
        }

        IsKillingPsmuxSession = true;
        PsmuxKillError = null;
        try
        {
            await psmuxService.KillSessionAsync(session.Name);

            TerminalTabViewModel[] matchingTabs = Tabs
                .Where(tab => tab.PsmuxSessionName?.Equals(
                    session.Name,
                    StringComparison.OrdinalIgnoreCase) == true)
                .ToArray();
            foreach (TerminalTabViewModel tab in matchingTabs)
            {
                await CloseTabAsync(tab);
            }

            IsPsmuxKillConfirmationOpen = false;
            PsmuxSessionPendingKill = null;
            StatusText = $"psmux session ‘{session.Name}’ ended";
            await RefreshPsmuxSessionsAsync();
        }
        catch (Exception exception) when (
            exception is TerminalLaunchException or InvalidOperationException or ArgumentException)
        {
            diagnostics.LogError(exception, "Failed to end psmux session {SessionName}.", session.Name);
            PsmuxKillError = exception.Message;
            StatusText = exception.Message;
        }
        finally
        {
            IsKillingPsmuxSession = false;
        }
    }

    [RelayCommand]
    private async Task AttachSelectedPsmuxSessionAsync()
    {
        PsmuxSessionItemViewModel? session = SelectedPsmuxSession;
        if (session is null)
        {
            return;
        }

        await OpenPsmuxSessionAsync(session);
        if (Tabs.Any(tab => tab.PsmuxSessionName?.Equals(
                session.Name,
                StringComparison.OrdinalIgnoreCase) == true))
        {
            IsPsmuxSessionsOpen = false;
        }
    }

    [RelayCommand]
    private async Task OpenPsmuxCreateAsync()
    {
        await RefreshPsmuxSessionsAsync();
        if (!IsPsmuxAvailable)
        {
            PsmuxAvailability availability = psmuxService is null
                ? new(false, null, null, "psmux integration is unavailable.")
                : await psmuxService.ProbeAsync();
            StatusText = availability.Error ??
                "psmux.exe was not found in PATH. Install it with ‘winget install psmux’.";
            return;
        }

        PsmuxSessionName = string.Empty;
        PsmuxError = null;
        IsPsmuxDuplicate = false;
        IsPsmuxCreateOpen = true;
    }

    [RelayCommand]
    private void CancelPsmuxCreate()
    {
        IsPsmuxCreateOpen = false;
        IsPsmuxDuplicate = false;
        PsmuxError = null;
    }

    [RelayCommand]
    private async Task ConfirmPsmuxCreateAsync()
    {
        string name = PsmuxSessionName.Trim();
        if (!PsmuxNamePattern.IsMatch(name))
        {
            PsmuxError = "Use 1–64 letters, numbers, underscores, or hyphens; start with a letter or number.";
            return;
        }

        PsmuxSessionItemViewModel? existing = PsmuxSessions.FirstOrDefault(
            session => session.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null && !IsPsmuxDuplicate)
        {
            IsPsmuxDuplicate = true;
            PsmuxError = $"Session ‘{existing.Name}’ already exists. Choose Attach existing to continue.";
            return;
        }

        IsPsmuxCreateOpen = false;
        if (existing is not null)
        {
            await OpenPsmuxSessionAsync(existing);
            return;
        }

        try
        {
            TerminalSessionDefinition definition =
                await psmuxService!.CreateSessionDefinitionAsync(name);
            AttachPsmuxTab(name, definition);
            if (!PsmuxSessions.Any(session =>
                    session.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                PsmuxSessions.Add(new PsmuxSessionItemViewModel(
                    new PsmuxSessionInfo(name, 1, true)));
            }

            StatusText = $"psmux session ‘{name}’ opened";
        }
        catch (Exception exception) when (
            exception is TerminalLaunchException or ArgumentException)
        {
            diagnostics.LogError(exception, "Failed to prepare a psmux session.");
            PsmuxError = exception.Message;
            IsPsmuxCreateOpen = true;
        }
    }

    [RelayCommand]
    private async Task OpenPsmuxSessionAsync(PsmuxSessionItemViewModel? session)
    {
        if (session is null || psmuxService is null)
        {
            return;
        }

        TerminalTabViewModel? openTab = Tabs.FirstOrDefault(tab =>
            tab.PsmuxSessionName?.Equals(session.Name, StringComparison.OrdinalIgnoreCase) == true);
        if (openTab is not null)
        {
            SelectedTab = openTab;
            openTab.RequestFocus();
            StatusText = $"psmux session ‘{session.Name}’ is already open";
            return;
        }

        try
        {
            TerminalSessionDefinition definition =
                await psmuxService.CreateAttachDefinitionAsync(session.Name);
            AttachPsmuxTab(session.Name, definition);
            StatusText = $"Attached to psmux session ‘{session.Name}’";
        }
        catch (Exception exception) when (
            exception is TerminalLaunchException or ArgumentException)
        {
            diagnostics.LogError(exception, "Failed to attach a psmux session.");
            StatusText = exception.Message;
            await RefreshPsmuxSessionsAsync();
        }
    }

    public async Task OpenSessionAsync(SessionListItemViewModel session)
    {
        ArgumentNullException.ThrowIfNull(session);

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
                settings.TerminalFontFamily,
                settings.TerminalFontSize,
                settings.TerminalSelectionColor,
                settings.TerminalCursorStyle,
                settings.TerminalCursorBlink,
                CloseTabAsync);

            AttachTab(tab);
            StatusText = $"Terminal prepared for ‘{session.Name}’";
        }
        catch (TerminalLaunchException exception)
        {
            diagnostics.LogError(exception, "Failed to prepare an SSH terminal.");
            StatusText = exception.Message;
            SettingsRequested?.Invoke(exception.Message);
        }
        catch (KeyNotFoundException exception)
        {
            diagnostics.LogWarning(exception, "A requested saved session was not found.");
            StatusText = exception.Message;
            SessionsRefreshRequested?.Invoke();
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
                settings.TerminalFontFamily,
                settings.TerminalFontSize,
                settings.TerminalSelectionColor,
                settings.TerminalCursorStyle,
                settings.TerminalCursorBlink,
                CloseTabAsync);

            AttachTab(tab);
            StatusText = $"Local terminal ‘{title}’ opened";
        }
        catch (TerminalLaunchException exception)
        {
            diagnostics.LogError(exception, "Failed to prepare a local terminal.");
            StatusText = exception.Message;
            SettingsRequested?.Invoke(exception.Message);
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelectedTab))]
    private Task CloseSelectedTabAsync() => CloseTabAsync(SelectedTab!);

    [RelayCommand(CanExecute = nameof(HasSelectedTab))]
    private void NextTab() => SelectRelativeTab(1);

    [RelayCommand(CanExecute = nameof(HasSelectedTab))]
    private void PreviousTab() => SelectRelativeTab(-1);

    public async Task SynchronizeTabsAsync(
        IReadOnlyList<SessionListItemViewModel> sessions)
    {
        foreach (TerminalTabViewModel tab in Tabs.ToArray())
        {
            if (tab.IsLocal)
            {
                continue;
            }

            SessionListItemViewModel? session = sessions.FirstOrDefault(
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

    public async Task ShutdownAsync()
    {
        foreach (TerminalTabViewModel tab in Tabs.ToArray())
        {
            await CloseTabAsync(tab);
        }
    }

    private bool HasSelectedTab() => SelectedTab is not null;

    private void AttachTab(TerminalTabViewModel tab)
    {
        tab.ApplicationCommandRequested += OnApplicationCommandRequested;
        tab.PtyStarted += OnTabPtyStarted;
        Tabs.Add(tab);
        SelectedTab = tab;
        OnPropertyChanged(nameof(HasOpenTabs));
    }

    private void AttachPsmuxTab(string name, TerminalSessionDefinition definition)
    {
        var tab = new TerminalTabViewModel(
            name,
            definition,
            ptySessionFactory,
            settings.TerminalFontFamily,
            settings.TerminalFontSize,
            settings.TerminalSelectionColor,
            settings.TerminalCursorStyle,
            settings.TerminalCursorBlink,
            CloseTabAsync);
        AttachTab(tab);
    }

    private void NotifyPsmuxSessionsChanged()
    {
        OnPropertyChanged(nameof(HasPsmuxSessions));
        OnPropertyChanged(nameof(HasPsmuxSessionsMessage));
    }

    public void MoveTab(
        TerminalTabViewModel tab,
        TerminalTabViewModel targetTab,
        bool insertAfter)
    {
        int sourceIndex = Tabs.IndexOf(tab);
        int targetIndex = Tabs.IndexOf(targetTab);
        if (sourceIndex < 0 || targetIndex < 0)
        {
            return;
        }

        int destinationIndex = targetIndex + (insertAfter ? 1 : 0);
        if (sourceIndex < destinationIndex)
        {
            destinationIndex--;
        }

        destinationIndex = Math.Clamp(destinationIndex, 0, Tabs.Count - 1);
        if (sourceIndex != destinationIndex)
        {
            Tabs.Move(sourceIndex, destinationIndex);
        }
    }

    public void RestoreTabAfterDrag(TerminalTabViewModel tab)
    {
        ArgumentNullException.ThrowIfNull(tab);
        if (!Tabs.Contains(tab))
        {
            return;
        }

        if (ReferenceEquals(SelectedTab, tab))
        {
            tab.RequestFocus();
        }
        else
        {
            SelectedTab = tab;
        }
    }

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
        catch (Exception exception) when (
            exception is InvalidOperationException or Win32Exception)
        {
            diagnostics.LogError(exception, "Failed to stop a terminal.");
            StatusText = $"Failed to stop PowerShell: {exception.Message}";
            return;
        }

        tab.ApplicationCommandRequested -= OnApplicationCommandRequested;
        tab.PtyStarted -= OnTabPtyStarted;
        bool wasSelected = ReferenceEquals(SelectedTab, tab);
        Tabs.RemoveAt(closedTabIndex);
        if (wasSelected)
        {
            int nextTabIndex = Math.Min(closedTabIndex, Tabs.Count - 1);
            SelectedTab = nextTabIndex >= 0 ? Tabs[nextTabIndex] : null;
        }

        OnPropertyChanged(nameof(HasOpenTabs));
        StatusText = $"Tab ‘{tab.Title}’ closed";
    }

    private async void OnApplicationCommandRequested(object? sender, string command)
    {
        switch (command)
        {
            case "closeTab" when sender is TerminalTabViewModel tab:
                await CloseTabAsync(tab);
                break;
            case "nextTab":
                NextTab();
                break;
            case "previousTab":
                PreviousTab();
                break;
            default:
                ApplicationCommandRequested?.Invoke(command);
                break;
        }
    }

    private async void OnTabPtyStarted(object? sender, EventArgs eventArgs)
    {
        if (sender is not TerminalTabViewModel { IsPsmux: true } tab ||
            tab.PsmuxSessionName is null)
        {
            return;
        }

        int[] delays = [100, 250, 500, 1000];
        foreach (int delay in delays)
        {
            await Task.Delay(delay);
            await RefreshPsmuxSessionsAsync();
            if (PsmuxSessions.Any(session => session.Name.Equals(
                    tab.PsmuxSessionName,
                    StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            if (Tabs.Contains(tab))
            {
                PsmuxSessions.Add(new PsmuxSessionItemViewModel(
                    new PsmuxSessionInfo(tab.PsmuxSessionName, 1, true)));
            }
        }
    }

}
