using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace HyperTerm.UI.ViewModels;

public sealed partial class MainWindowViewModel
{
    private const int PaletteResultLimit = 50;
    private Action? cancelPaletteRefresh;

    public ObservableCollection<CommandPaletteItemViewModel> CommandPaletteResults { get; } = [];

    public bool HasCommandPaletteResults => CommandPaletteResults.Count > 0;

    [ObservableProperty]
    private bool isCommandPaletteOpen;

    [ObservableProperty]
    private string commandPaletteQuery = string.Empty;

    [ObservableProperty]
    private CommandPaletteItemViewModel? selectedCommandPaletteItem;

    partial void OnIsCommandPaletteOpenChanged(bool value)
    {
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
        CommandPaletteQuery = string.Empty;
        IsCommandPaletteOpen = true;
        RebuildCommandPalette();
        Observe(
            RefreshPalettePsmuxSessionsAsync(refreshCancellation, cancelRefresh),
            "refresh command palette psmux sessions");
    }

    [RelayCommand]
    private void CloseCommandPalette()
    {
        IsCommandPaletteOpen = false;
        CommandPaletteQuery = string.Empty;
        CommandPaletteResults.Clear();
        SelectedCommandPaletteItem = null;
        Workspace.SelectedTab?.RequestFocus();
    }

    [RelayCommand]
    private async Task ExecuteSelectedCommandPaletteItemAsync()
    {
        CommandPaletteItemViewModel? item = SelectedCommandPaletteItem;
        if (item is null)
        {
            return;
        }

        CloseCommandPalette();
        await item.ExecuteAsync();
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

        string query = CommandPaletteQuery.Trim();
        IEnumerable<CommandPaletteItemViewModel> candidates = BuildPaletteCandidates();
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
        OnPropertyChanged(nameof(HasCommandPaletteResults));
    }

    private IEnumerable<CommandPaletteItemViewModel> BuildPaletteCandidates()
    {
        foreach (CommandPaletteItemViewModel action in BuildActionCandidates())
        {
            yield return action;
        }

        foreach (SessionListItemViewModel session in Explorer.Sessions)
        {
            yield return new CommandPaletteItemViewModel(
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

        foreach (PsmuxSessionItemViewModel session in Workspace.PsmuxSessions)
        {
            yield return new CommandPaletteItemViewModel(
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
        yield return AsyncAction("New PowerShell terminal", "Open a local terminal", 1,
            () => Workspace.OpenLocalTerminalCommand.ExecuteAsync(null));
        yield return AsyncAction("Create psmux session", "Start a persistent terminal", 2,
            () => Workspace.OpenPsmuxCreateCommand.ExecuteAsync(null));
        yield return AsyncAction("List psmux sessions", "View persistent terminals", 3,
            () => Workspace.OpenPsmuxSessionsCommand.ExecuteAsync(null));
        yield return Action("Open settings", "Configure HyperTerm", 4,
            () => Settings.OpenSettingsCommand.Execute(null));
        yield return Action("Show keyboard shortcuts", "View all shortcuts", 5,
            () => IsShortcutsOpen = true);
        yield return Action("Toggle sidebar", "Show or hide saved sessions", 6,
            () => IsSidebarVisible = !IsSidebarVisible);
        yield return Action("Toggle status bar", "Show or hide terminal status", 7,
            () => IsStatusBarVisible = !IsStatusBarVisible);
    }

    private static CommandPaletteItemViewModel Action(
        string title,
        string subtitle,
        int order,
        Action execute) =>
        new("Action", title, subtitle, $"{title} {subtitle}", order, () =>
        {
            execute();
            return Task.CompletedTask;
        });

    private static CommandPaletteItemViewModel AsyncAction(
        string title,
        string subtitle,
        int order,
        Func<Task> execute) =>
        new("Action", title, subtitle, $"{title} {subtitle}", order, execute);

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
