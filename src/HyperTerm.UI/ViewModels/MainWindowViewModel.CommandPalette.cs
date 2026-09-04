using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HyperTerm.Core.Models;

namespace HyperTerm.UI.ViewModels;

public sealed partial class MainWindowViewModel
{
    private enum CommandPaletteMode
    {
        All,
        Commands,
        OpenSessions,
    }

    private enum CommandPaletteScope
    {
        Root,
        TerminalProfiles,
        SplitTargets,
    }

    private const int PaletteResultLimit = 50;
    private CommandPaletteScope commandPaletteScope;
    private SplitOrientation? pendingSplitOrientation;
    private bool returnToPaletteRootAfterSplit;

    public ObservableCollection<CommandPaletteItemViewModel> CommandPaletteResults { get; } = [];

    public bool HasCommandPaletteResults => CommandPaletteResults.Count > 0;

    public bool IsCommandPaletteProfileSelection =>
        commandPaletteScope == CommandPaletteScope.TerminalProfiles;

    public bool IsCommandPaletteSplitSelection =>
        commandPaletteScope == CommandPaletteScope.SplitTargets;

    public bool CanReturnToCommandPaletteRoot =>
        IsCommandPaletteProfileSelection ||
        (IsCommandPaletteSplitSelection && returnToPaletteRootAfterSplit);

    public string CommandPalettePlaceholder => commandPaletteScope switch
    {
        CommandPaletteScope.TerminalProfiles => "Search terminal profiles…",
        CommandPaletteScope.SplitTargets => pendingSplitOrientation == SplitOrientation.Vertical
            ? "Split right: choose terminal or SSH session…"
            : "Split down: choose terminal or SSH session…",
        _ => "Search all…  > commands  : open sessions",
    };

    public string CommandPaletteFooterText => commandPaletteScope switch
    {
        CommandPaletteScope.TerminalProfiles => "Esc Back   ↑↓ Navigate   Enter Open",
        CommandPaletteScope.SplitTargets when returnToPaletteRootAfterSplit =>
            "Esc Back   ↑↓ Navigate   Enter Split",
        CommandPaletteScope.SplitTargets => "Esc Cancel   ↑↓ Navigate   Enter Split",
        _ => "> Commands   : Open sessions   ↑↓ Navigate   Enter Run   Esc Close",
    };

    [ObservableProperty]
    private string commandPaletteEmptyMessage = "No matching commands or resources.";

    [ObservableProperty]
    private bool isCommandPaletteOpen;

    [ObservableProperty]
    private string commandPaletteQuery = string.Empty;

    [ObservableProperty]
    private CommandPaletteItemViewModel? selectedCommandPaletteItem;

    partial void OnIsCommandPaletteOpenChanged(bool value)
    {
        if (value)
        {
            CoordinateOverlayOpening(OverlayKind.CommandPalette);
        }

        NotifyTerminalVisibilityChanged();
    }

    partial void OnCommandPaletteQueryChanged(string value) =>
        RebuildCommandPalette();

    [RelayCommand]
    private void OpenCommandPalette()
    {
        pendingSplitOrientation = null;
        returnToPaletteRootAfterSplit = false;
        SetCommandPaletteScope(CommandPaletteScope.Root);
        CommandPaletteQuery = string.Empty;
        IsCommandPaletteOpen = true;
        RebuildCommandPalette();
    }

    [RelayCommand]
    private void CloseCommandPalette() => CloseCommandPalette(restoreTerminalFocus: true);

    private void CloseCommandPalette(bool restoreTerminalFocus)
    {
        IsCommandPaletteOpen = false;
        CommandPaletteQuery = string.Empty;
        pendingSplitOrientation = null;
        returnToPaletteRootAfterSplit = false;
        SetCommandPaletteScope(CommandPaletteScope.Root);
        CommandPaletteResults.Clear();
        SelectedCommandPaletteItem = null;
        if (restoreTerminalFocus)
        {
            Workspace.SelectedTab?.RequestFocus();
        }
    }

    [RelayCommand]
    private async Task ExecuteSelectedCommandPaletteItemAsync()
    {
        CommandPaletteItemViewModel? item = SelectedCommandPaletteItem;
        if (item is null)
        {
            return;
        }

        if (item.ClosesPaletteOnExecute)
        {
            CloseCommandPalette(item.RestoreTerminalFocusOnClose);
        }

        await item.ExecuteAsync();
    }

    [RelayCommand]
    private void ReturnToCommandPaletteRoot()
    {
        if (!CanReturnToCommandPaletteRoot)
        {
            return;
        }

        pendingSplitOrientation = null;
        returnToPaletteRootAfterSplit = false;
        SetCommandPaletteScope(CommandPaletteScope.Root);
        CommandPaletteQuery = string.Empty;
        RebuildCommandPalette();
    }

    internal void HandleCommandPaletteEscape()
    {
        if (CanReturnToCommandPaletteRoot)
        {
            ReturnToCommandPaletteRoot();
        }
        else
        {
            CloseCommandPalette();
        }
    }

    internal void MoveCommandPaletteSelection(int offset)
    {
        if (CommandPaletteResults.Count == 0)
        {
            return;
        }

        int currentIndex = SelectedCommandPaletteItem is null
            ? 0
            : CommandPaletteResults.IndexOf(SelectedCommandPaletteItem);
        int nextIndex = (currentIndex + offset + CommandPaletteResults.Count) %
            CommandPaletteResults.Count;
        SelectedCommandPaletteItem = CommandPaletteResults[nextIndex];
    }

    private void RebuildCommandPalette()
    {
        if (!IsCommandPaletteOpen)
        {
            return;
        }

        bool selectingProfile = IsCommandPaletteProfileSelection;
        bool selectingSplitTarget = IsCommandPaletteSplitSelection;
        (CommandPaletteMode mode, string query) = selectingProfile || selectingSplitTarget
            ? (CommandPaletteMode.All, CommandPaletteQuery.Trim())
            : ParsePaletteQuery(CommandPaletteQuery);
        IEnumerable<CommandPaletteItemViewModel> candidates = commandPaletteScope switch
        {
            CommandPaletteScope.TerminalProfiles => BuildTerminalProfileCandidates(),
            CommandPaletteScope.SplitTargets => BuildSplitTargetCandidates(),
            _ => FilterPaletteCandidates(BuildPaletteCandidates(), mode),
        };
        IEnumerable<CommandPaletteItemViewModel> results = query.Length == 0
            ? candidates.OrderBy(item => item.DisplayOrder).ThenBy(item => item.Title, StringComparer.CurrentCultureIgnoreCase)
            : candidates
                .Select(item => (Item: item, Score: ScorePaletteMatch(item.SearchText, query)))
                .Where(result => result.Score.HasValue)
                .OrderBy(result => result.Score)
                .ThenBy(result => result.Item.DisplayOrder)
                .ThenBy(result => result.Item.Title, StringComparer.CurrentCultureIgnoreCase)
                .Select(result => result.Item);

        CommandPaletteResults.Clear();
        foreach (CommandPaletteItemViewModel result in results.Take(PaletteResultLimit))
        {
            CommandPaletteResults.Add(result);
        }

        SelectedCommandPaletteItem = CommandPaletteResults.FirstOrDefault();
        CommandPaletteEmptyMessage = commandPaletteScope switch
        {
            CommandPaletteScope.TerminalProfiles => "No matching terminal profiles.",
            CommandPaletteScope.SplitTargets => "No matching terminals or SSH sessions.",
            _ => mode switch
            {
                CommandPaletteMode.Commands => "No matching commands.",
                CommandPaletteMode.OpenSessions => "No matching open sessions.",
                _ => "No matching commands or resources.",
            },
        };
        OnPropertyChanged(nameof(HasCommandPaletteResults));
    }

    private void SetCommandPaletteScope(CommandPaletteScope value)
    {
        if (commandPaletteScope == value)
        {
            return;
        }

        commandPaletteScope = value;
        OnPropertyChanged(nameof(IsCommandPaletteProfileSelection));
        OnPropertyChanged(nameof(IsCommandPaletteSplitSelection));
        OnPropertyChanged(nameof(CanReturnToCommandPaletteRoot));
        OnPropertyChanged(nameof(CommandPalettePlaceholder));
        OnPropertyChanged(nameof(CommandPaletteFooterText));
    }

    private void OpenTerminalProfileSelection()
    {
        SetCommandPaletteScope(CommandPaletteScope.TerminalProfiles);
        CommandPaletteQuery = string.Empty;
        RebuildCommandPalette();
    }

    private void OpenSplitTargetSelection(SplitOrientation orientation)
    {
        returnToPaletteRootAfterSplit =
            IsCommandPaletteOpen && commandPaletteScope == CommandPaletteScope.Root;
        pendingSplitOrientation = orientation;
        SetCommandPaletteScope(CommandPaletteScope.SplitTargets);
        CommandPaletteQuery = string.Empty;
        IsCommandPaletteOpen = true;
        RebuildCommandPalette();
    }

    private static (CommandPaletteMode Mode, string Query) ParsePaletteQuery(string value)
    {
        string query = value.Trim();
        if (query.Length == 0)
        {
            return (CommandPaletteMode.All, string.Empty);
        }

        return query[0] switch
        {
            '>' => (CommandPaletteMode.Commands, query[1..].Trim()),
            ':' => (CommandPaletteMode.OpenSessions, query[1..].Trim()),
            _ => (CommandPaletteMode.All, query),
        };
    }

    private static IEnumerable<CommandPaletteItemViewModel> FilterPaletteCandidates(
        IEnumerable<CommandPaletteItemViewModel> candidates,
        CommandPaletteMode mode) =>
        mode switch
        {
            CommandPaletteMode.Commands => candidates.Where(
                item => item.Kind == CommandPaletteItemKind.Action),
            CommandPaletteMode.OpenSessions => candidates.Where(
                item => item.Kind == CommandPaletteItemKind.OpenTab),
            _ => candidates,
        };

    private IEnumerable<CommandPaletteItemViewModel> BuildPaletteCandidates()
    {
        foreach (CommandPaletteItemViewModel action in BuildActionCandidates())
        {
            yield return action;
        }

        foreach (SessionListItemViewModel session in Explorer.Sessions)
        {
            yield return new CommandPaletteItemViewModel(
                CommandPaletteItemKind.SavedSshSession,
                "SSH session",
                session.Name,
                session.Endpoint,
                $"{session.Name} {session.Endpoint} {session.Folder} {session.Notes}",
                20,
                () => Workspace.OpenSessionAsync(session));
        }

        foreach (TerminalTabViewModel tab in Workspace.Tabs)
        {
            yield return new CommandPaletteItemViewModel(
                CommandPaletteItemKind.OpenTab,
                "Open tab",
                tab.Title,
                tab.Endpoint,
                $"{tab.Title} {tab.Endpoint} {tab.Folder}",
                40,
                () =>
                {
                    Workspace.SelectedTab = tab;
                    tab.RequestFocus();
                    return Task.CompletedTask;
                });
        }

    }

    private IEnumerable<CommandPaletteItemViewModel> BuildActionCandidates()
    {
        yield return Action("New SSH session", "Create a saved connection", 0,
            () => SessionEditor.OpenNew(string.Empty));
        yield return Action(
            "New Terminal",
            "Choose a local terminal profile",
            1,
            OpenTerminalProfileSelection,
            closesPaletteOnExecute: false);

        yield return Action("Open settings", "Configure HyperTerm", 22,
            () => Settings.OpenSettingsCommand.Execute(null));
        yield return Action("Show keyboard shortcuts", "View all shortcuts", 23,
            () => IsShortcutsOpen = true);
        yield return Action("Search terminal", "Find text in the active terminal", 24,
            OpenTerminalSearch, restoreTerminalFocusOnClose: false);
        yield return Action("Toggle sidebar", "Show or hide saved sessions", 25,
            () => IsSidebarVisible = !IsSidebarVisible);
        yield return Action("Toggle status bar", "Show or hide terminal status", 26,
            () => IsStatusBarVisible = !IsStatusBarVisible);
        yield return Action(
            "Terminal: Split Right",
            "Choose a terminal or SSH session for a vertical split",
            27,
            () => Workspace.SplitRightCommand.Execute(null),
            closesPaletteOnExecute: false);
        yield return Action(
            "Terminal: Split Down",
            "Choose a terminal or SSH session for a horizontal split",
            28,
            () => Workspace.SplitDownCommand.Execute(null),
            closesPaletteOnExecute: false);
        yield return AsyncAction("Terminal: Close Pane", "Close the active terminal pane", 29,
            () => Workspace.ClosePaneCommand.ExecuteAsync(null));
        yield return Action("Terminal: Focus Next Pane", "Focus the next terminal pane", 30,
            () => Workspace.FocusNextPaneCommand.Execute(null));
        yield return Action("Terminal: Focus Previous Pane", "Focus the previous terminal pane", 31,
            () => Workspace.FocusPreviousPaneCommand.Execute(null));
    }

    private IEnumerable<CommandPaletteItemViewModel> BuildTerminalProfileCandidates()
    {
        int displayOrder = 0;
        foreach (TerminalLaunchProfileViewModel profile in Workspace.TerminalProfiles.Where(
                     profile => profile.IsAvailable))
        {
            TerminalLaunchProfileViewModel selectedProfile = profile;
            string subtitle = profile.IsDefault
                ? "Default terminal profile"
                : "Local terminal profile";
            yield return new CommandPaletteItemViewModel(
                CommandPaletteItemKind.TerminalProfile,
                "Terminal profile",
                profile.Name,
                subtitle,
                $"{profile.Name} {subtitle}",
                displayOrder++,
                () => Workspace.OpenTerminalProfileCommand.ExecuteAsync(selectedProfile));
        }
    }

    private IEnumerable<CommandPaletteItemViewModel> BuildSplitTargetCandidates()
    {
        if (pendingSplitOrientation is not SplitOrientation orientation)
        {
            yield break;
        }

        int displayOrder = 0;
        foreach (TerminalLaunchProfileViewModel profile in Workspace.TerminalProfiles.Where(
                     profile => profile.IsAvailable))
        {
            TerminalLaunchProfileViewModel selectedProfile = profile;
            string subtitle = profile.IsDefault
                ? "Default terminal profile"
                : "Local terminal profile";
            yield return new CommandPaletteItemViewModel(
                CommandPaletteItemKind.TerminalProfile,
                "Terminal profile",
                profile.Name,
                subtitle,
                $"terminal profile {profile.Name} {subtitle}",
                displayOrder++,
                () => Workspace.SplitWithTerminalProfileAsync(orientation, selectedProfile));
        }

        displayOrder = 1000;
        foreach (SessionListItemViewModel session in Explorer.Sessions)
        {
            SessionListItemViewModel selectedSession = session;
            yield return new CommandPaletteItemViewModel(
                CommandPaletteItemKind.SavedSshSession,
                "SSH session",
                session.Name,
                session.Endpoint,
                $"SSH session {session.Name} {session.Endpoint} {session.Folder} {session.Notes}",
                displayOrder++,
                () => Workspace.SplitWithSessionAsync(orientation, selectedSession));
        }
    }

    private static CommandPaletteItemViewModel Action(
        string title,
        string subtitle,
        int order,
        Action execute,
        bool restoreTerminalFocusOnClose = true,
        bool closesPaletteOnExecute = true) =>
        new(
            CommandPaletteItemKind.Action,
            "Action",
            title,
            subtitle,
            $"{title} {subtitle}",
            order,
            () =>
            {
                execute();
                return Task.CompletedTask;
            },
            restoreTerminalFocusOnClose,
            closesPaletteOnExecute);

    private static CommandPaletteItemViewModel AsyncAction(
        string title,
        string subtitle,
        int order,
        Func<Task> execute) =>
        new(
            CommandPaletteItemKind.Action,
            "Action",
            title,
            subtitle,
            $"{title} {subtitle}",
            order,
            execute);

    private static int? ScorePaletteMatch(string candidate, string query)
    {
        if (candidate.Equals(query, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (candidate.StartsWith(query, StringComparison.OrdinalIgnoreCase))
        {
            return 100 + candidate.Length - query.Length;
        }

        int matchIndex = candidate.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (matchIndex >= 0)
        {
            bool wordStart = matchIndex == 0 || !char.IsLetterOrDigit(candidate[matchIndex - 1]);
            return (wordStart ? 200 : 300) + matchIndex;
        }

        int candidateIndex = 0;
        int gapCount = 0;
        foreach (char queryCharacter in query)
        {
            int foundIndex = candidate.IndexOf(
                queryCharacter.ToString(),
                candidateIndex,
                StringComparison.OrdinalIgnoreCase);
            if (foundIndex < 0)
            {
                return null;
            }

            gapCount += foundIndex - candidateIndex;
            candidateIndex = foundIndex + 1;
        }

        return 500 + gapCount;
    }
}
