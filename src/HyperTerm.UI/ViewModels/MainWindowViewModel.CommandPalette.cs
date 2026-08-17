using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

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
    }

    private const int PaletteResultLimit = 50;
    private Action? cancelPaletteRefresh;
    private CommandPaletteScope commandPaletteScope;

    public ObservableCollection<CommandPaletteItemViewModel> CommandPaletteResults { get; } = [];

    public bool HasCommandPaletteResults => CommandPaletteResults.Count > 0;

    public bool IsCommandPaletteProfileSelection =>
        commandPaletteScope == CommandPaletteScope.TerminalProfiles;

    public string CommandPalettePlaceholder => IsCommandPaletteProfileSelection
        ? "Search terminal profiles…"
        : "Search all…  > commands  : open sessions";

    public string CommandPaletteFooterText => IsCommandPaletteProfileSelection
        ? "Esc Back   ↑↓ Navigate   Enter Open"
        : "> Commands   : Open sessions   ↑↓ Navigate   Enter Run   Esc Close";

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
        if (!value)
        {
            cancelPaletteRefresh?.Invoke();
        }
    }

    partial void OnCommandPaletteQueryChanged(string value) =>
        RebuildCommandPalette();

    [RelayCommand]
    private void OpenCommandPalette()
    {
        cancelPaletteRefresh?.Invoke();
        var refreshCancellation = new CancellationTokenSource();
        Action cancelRefresh = refreshCancellation.Cancel;
        cancelPaletteRefresh = cancelRefresh;
        SetCommandPaletteScope(CommandPaletteScope.Root);
        CommandPaletteQuery = string.Empty;
        IsCommandPaletteOpen = true;
        RebuildCommandPalette();
        if (Workspace.IsPsmuxEnabled)
        {
            Observe(
                RefreshPalettePsmuxSessionsAsync(refreshCancellation, cancelRefresh),
                "refresh command palette psmux sessions");
        }
        else
        {
            cancelPaletteRefresh = null;
            refreshCancellation.Dispose();
        }
    }

    [RelayCommand]
    private void CloseCommandPalette() => CloseCommandPalette(restoreTerminalFocus: true);

    private void CloseCommandPalette(bool restoreTerminalFocus)
    {
        IsCommandPaletteOpen = false;
        CommandPaletteQuery = string.Empty;
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
        if (!IsCommandPaletteProfileSelection)
        {
            return;
        }

        SetCommandPaletteScope(CommandPaletteScope.Root);
        CommandPaletteQuery = string.Empty;
        RebuildCommandPalette();
    }

    internal void HandleCommandPaletteEscape()
    {
        if (IsCommandPaletteProfileSelection)
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

    private async Task RefreshPalettePsmuxSessionsAsync(
        CancellationTokenSource cancellation,
        Action cancelRefresh)
    {
        CancellationToken cancellationToken = cancellation.Token;
        try
        {
            await Workspace.RefreshPsmuxSessionsAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (IsCommandPaletteOpen)
            {
                RebuildCommandPalette();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Closing the palette cancels its optional background refresh.
        }
        finally
        {
            if (ReferenceEquals(cancelPaletteRefresh, cancelRefresh))
            {
                cancelPaletteRefresh = null;
            }

            cancellation.Dispose();
        }
    }

    private void RebuildCommandPalette()
    {
        if (!IsCommandPaletteOpen)
        {
            return;
        }

        bool selectingProfile = IsCommandPaletteProfileSelection;
        (CommandPaletteMode mode, string query) = selectingProfile
            ? (CommandPaletteMode.All, CommandPaletteQuery.Trim())
            : ParsePaletteQuery(CommandPaletteQuery);
        IEnumerable<CommandPaletteItemViewModel> candidates = selectingProfile
            ? BuildTerminalProfileCandidates()
            : FilterPaletteCandidates(BuildPaletteCandidates(), mode);
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
        CommandPaletteEmptyMessage = selectingProfile
            ? "No matching terminal profiles."
            : mode switch
            {
                CommandPaletteMode.Commands => "No matching commands.",
                CommandPaletteMode.OpenSessions => "No matching open sessions.",
                _ => "No matching commands or resources.",
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
        OnPropertyChanged(nameof(CommandPalettePlaceholder));
        OnPropertyChanged(nameof(CommandPaletteFooterText));
    }

    private void OpenTerminalProfileSelection()
    {
        SetCommandPaletteScope(CommandPaletteScope.TerminalProfiles);
        CommandPaletteQuery = string.Empty;
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

        foreach (PsmuxSessionItemViewModel session in Workspace.PsmuxSessions.Where(
                     _ => Workspace.IsPsmuxEnabled))
        {
            yield return new CommandPaletteItemViewModel(
                CommandPaletteItemKind.PsmuxSession,
                "psmux session",
                session.Name,
                session.Details,
                $"psmux {session.Name} {session.Details}",
                50,
                () => Workspace.OpenPsmuxSessionCommand.ExecuteAsync(session));
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

        if (Workspace.IsPsmuxEnabled)
        {
            yield return AsyncAction("Create psmux session", "Start a persistent terminal", 20,
                () => Workspace.OpenPsmuxCreateCommand.ExecuteAsync(null));
            yield return AsyncAction("List psmux sessions", "View persistent terminals", 21,
                () => Workspace.OpenPsmuxSessionsCommand.ExecuteAsync(null));
        }
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
        yield return AsyncAction("Terminal: Split Right", "Split the active pane vertically", 27,
            () => Workspace.SplitRightCommand.ExecuteAsync(null));
        yield return AsyncAction("Terminal: Split Down", "Split the active pane horizontally", 28,
            () => Workspace.SplitDownCommand.ExecuteAsync(null));
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
