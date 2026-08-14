using HyperTerm.UI.ViewModels;

namespace HyperTerm.UI.Controls;

internal interface ITerminalSurface
{
    Task CreateAsync(TerminalTabViewModel tab, TerminalPaneViewModel pane);

    Task ConfigureAsync(TerminalTabViewModel tab);

    Task LayoutAsync(TerminalTabViewModel tab);

    Task DisposeAsync(Guid paneId);

    Task ActivateAsync(Guid paneId);

    Task FocusAsync(Guid paneId);

    Task OpenSearchAsync(Guid paneId);

    Task WriteAsync(Guid paneId, long token, string output);
}
