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
        settings = TerminalProfileCatalog.Normalize(value);
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
    }

    public void SetStatus(string value) => StatusText = value;
}
