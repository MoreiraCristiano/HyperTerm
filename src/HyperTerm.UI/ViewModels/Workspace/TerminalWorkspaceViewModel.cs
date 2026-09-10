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
    ILogger<TerminalWorkspaceViewModel>? logger = null,
    ITerminalProfileResolver? terminalProfileResolver = null) : ViewModelBase
{
    private ApplicationSettings settings = new();
    private readonly ILogger<TerminalWorkspaceViewModel> diagnostics =
        logger ?? NullLogger<TerminalWorkspaceViewModel>.Instance;

    public event Action<string>? ApplicationCommandRequested;

    public event Action<string>? SettingsRequested;

    public event Action? SessionsRefreshRequested;

    public event Action<SplitOrientation>? SplitSelectionRequested;

    public ObservableCollection<TerminalTabViewModel> Tabs { get; } = [];

    public ObservableCollection<TerminalLaunchProfileViewModel> TerminalProfiles { get; } = [];

    public string Title => "HyperTerm";

    public bool HasOpenTabs => Tabs.Count > 0;

    [ObservableProperty]
    private TerminalTabViewModel? selectedTab;

    [ObservableProperty]
    private string statusText = "Ready";

    [ObservableProperty]
    private string terminalStatusText = "Terminal";

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
        SplitActiveTerminalCommand.NotifyCanExecuteChanged();
        NextTabCommand.NotifyCanExecuteChanged();
        PreviousTabCommand.NotifyCanExecuteChanged();
    }

    public void ApplySettings(ApplicationSettings value)
    {
        ApplySettingsCore(value);
    }

    public Task ApplySettingsAsync(ApplicationSettings value)
    {
        ApplySettingsCore(value);
        return Task.CompletedTask;
    }

    private void ApplySettingsCore(ApplicationSettings value)
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
