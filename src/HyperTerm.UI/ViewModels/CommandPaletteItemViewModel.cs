namespace HyperTerm.UI.ViewModels;

public sealed class CommandPaletteItemViewModel(
    string category,
    string title,
    string subtitle,
    string searchText,
    int displayOrder,
    Func<Task> executeAsync)
{
    private readonly Func<Task> execute = executeAsync;

    public string Category { get; } = category;

    public string Title { get; } = title;

    public string Subtitle { get; } = subtitle;

    public string SearchText { get; } = searchText;

    public int DisplayOrder { get; } = displayOrder;

    public Task ExecuteAsync() => execute();
}
