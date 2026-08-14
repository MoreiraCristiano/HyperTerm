using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HyperTerm.Core.Abstractions.Services;
using HyperTerm.Core.Abstractions.Terminal;
using HyperTerm.Core.Entities;
using HyperTerm.Core.Exceptions;
using HyperTerm.Core.Models;
using HyperTerm.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HyperTerm.UI.ViewModels;

public sealed partial class TerminalWorkspaceViewModel(
    ISessionService sessionService,
    ITerminalSessionFactory terminalSessionFactory,
    IPtySessionFactory ptySessionFactory,
    IPsmuxService? psmuxService = null,
    ILogger<TerminalWorkspaceViewModel>? logger = null,
    ITerminalProfileResolver? terminalProfileResolver = null) : ViewModelBase
{
    private ApplicationSettings settings = new();
    private bool hasAppliedSettings;
    private readonly ILogger<TerminalWorkspaceViewModel> diagnostics =
        logger ?? NullLogger<TerminalWorkspaceViewModel>.Instance;

    public event Action<string>? ApplicationCommandRequested;

    public event Action<string>? SettingsRequested;

    public event Action? SessionsRefreshRequested;

    public ObservableCollection<TerminalTabViewModel> Tabs { get; } = [];

    public ObservableCollection<PsmuxSessionItemViewModel> PsmuxSessions { get; } = [];

    public ObservableCollection<TerminalLaunchProfileViewModel> TerminalProfiles { get; } = [];

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
    private string terminalStatusText = "Terminal";

    [ObservableProperty]
    private bool isPsmuxAvailable;

    [ObservableProperty]
    private bool isPsmuxEnabled = true;

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
        Task cleanup = ApplySettingsCore(value);
        if (!cleanup.IsCompletedSuccessfully)
        {
            Observe(cleanup, "disable psmux integration");
        }
    }

    public async Task ApplySettingsAsync(ApplicationSettings value)
    {
        Task cleanup = ApplySettingsCore(value);
        await cleanup;
    }

    private Task ApplySettingsCore(ApplicationSettings value)
    {
        ArgumentNullException.ThrowIfNull(value);
        bool disablePsmux = hasAppliedSettings && IsPsmuxEnabled && !value.PsmuxEnabled;
        settings = TerminalProfileCatalog.Normalize(value);
        hasAppliedSettings = true;
        IsPsmuxEnabled = settings.PsmuxEnabled;
        if (!IsPsmuxEnabled)
        {
            ResetPsmuxState();
        }

        TerminalProfiles.Clear();
        foreach (TerminalProfile profile in settings.TerminalProfiles)
        {
            bool isAvailable = terminalProfileResolver?.TryResolve(profile.ExecutablePath) is not null ||
                terminalProfileResolver is null;
            TerminalProfiles.Add(new TerminalLaunchProfileViewModel(
                profile,
                isAvailable,
                profile.Id.Equals(
                    settings.DefaultTerminalProfileId,
                    StringComparison.OrdinalIgnoreCase)));
        }

        TerminalProfile defaultProfile = TerminalProfileCatalog.GetProfile(settings);
        TerminalStatusText = $"Default: {defaultProfile.Name}";

        foreach (TerminalTabViewModel tab in Tabs)
        {
            tab.UpdateAppearance(
                settings.TerminalFontFamily,
                settings.TerminalFontSize,
                settings.TerminalSelectionColor,
                settings.TerminalCursorStyle,
                settings.TerminalCursorBlink,
                settings.Theme);
        }

        return disablePsmux ? DisablePsmuxAsync() : Task.CompletedTask;
    }

    private async Task DisablePsmuxAsync()
    {
        foreach (TerminalTabViewModel tab in Tabs.Where(tab => tab.IsPsmux).ToArray())
        {
            await CloseTabAsync(tab);
        }

        if (Tabs.Any(tab => tab.IsPsmux))
        {
            StatusText = "psmux integration disabled; a psmux tab could not be closed";
            return;
        }

        if (psmuxService is null)
        {
            return;
        }

        bool stopped = await psmuxService.TryStopServerAsync();
        StatusText = stopped
            ? "psmux integration disabled"
            : "psmux integration disabled; sessions attached by another client were preserved";
    }

    private void ResetPsmuxState()
    {
        IsPsmuxAvailable = false;
        IsPsmuxSessionsOpen = false;
        IsPsmuxCreateOpen = false;
        IsPsmuxKillConfirmationOpen = false;
        PsmuxSessionPendingKill = null;
        PsmuxKillError = null;
        PsmuxError = null;
        PsmuxSessions.Clear();
        SelectedPsmuxSession = null;
        PsmuxSessionsMessage = null;
        NotifyPsmuxSessionsChanged();
    }

    public void SetStatus(string value) => StatusText = value;
}
