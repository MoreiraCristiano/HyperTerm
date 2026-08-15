namespace HyperTerm.UI.ViewModels;

public enum CommandPaletteItemKind
{
    Action,
    SavedSshSession,
    OpenTab,
    PsmuxSession,
}

public sealed class CommandPaletteItemViewModel(
    CommandPaletteItemKind kind,
    string category,
    string title,
    string subtitle,
    string searchText,
    int displayOrder,
    Func<Task> executeAsync,
    bool restoreTerminalFocusOnClose = true)
{
    private readonly Func<Task> execute = executeAsync;

    public CommandPaletteItemKind Kind { get; } = kind;

    public string Category { get; } = category;

    public string Title { get; } = title;

    public string Subtitle { get; } = subtitle;

    public string SearchText { get; } = searchText;

    public int DisplayOrder { get; } = displayOrder;

    public bool RestoreTerminalFocusOnClose { get; } = restoreTerminalFocusOnClose;

    public Task ExecuteAsync() => execute();
}
