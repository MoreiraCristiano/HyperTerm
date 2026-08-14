using System.Text.Json;
using HyperTerm.UI.ViewModels;

namespace HyperTerm.UI.Controls;

internal sealed class WebTerminalScriptBridge(Func<string, Task> invokeScript) : ITerminalSurface
{
    public Task CreateAsync(TerminalTabViewModel tab) =>
        CreateAsync(tab, tab.ActivePane ?? throw new ArgumentException("Tab has no pane.", nameof(tab)));

    public Task CreateAsync(TerminalTabViewModel tab, TerminalPaneViewModel pane) =>
        invokeScript($"window.terminalHost.create({CreateRequest(tab, pane)})");

    public Task ConfigureAsync(TerminalTabViewModel tab) =>
        invokeScript($"window.terminalHost.configureTab({CreateTabOptions(tab)})");

    public Task LayoutAsync(TerminalTabViewModel tab) =>
        invokeScript($"window.terminalHost.layout({CreateLayoutRequest(tab)})");

    public Task DisposeAsync(Guid tabId) =>
        invokeScript($"window.terminalHost.dispose({SerializeTerminalId(tabId)})");

    public Task ActivateAsync(Guid tabId) =>
        invokeScript($"window.terminalHost.activate({SerializeTerminalId(tabId)})");

    public Task FocusAsync(Guid tabId) =>
        invokeScript($"window.terminalHost.focus({SerializeTerminalId(tabId)})");

    public Task OpenSearchAsync(Guid tabId) =>
        invokeScript($"window.terminalHost.openSearch({SerializeTerminalId(tabId)})");

    public Task WriteAsync(Guid tabId, long token, string output) =>
        invokeScript(
            $"window.terminalHost.write({SerializeTerminalId(tabId)}, {token}, " +
            $"{JsonSerializer.Serialize(output)})");

    private static string CreateRequest(
        TerminalTabViewModel tab,
        TerminalPaneViewModel pane) =>
        JsonSerializer.Serialize(new
        {
            paneId = GetTerminalId(pane.PaneId),
            tabId = GetTerminalId(tab.Id),
            options = CreateOptions(tab),
        });

    private static string CreateTabOptions(TerminalTabViewModel tab) =>
        JsonSerializer.Serialize(new
        {
            tabId = GetTerminalId(tab.Id),
            options = CreateOptions(tab),
        });

    private static object CreateOptions(TerminalTabViewModel tab) => new
    {
        fontFamily = tab.FontFamily,
        fontSize = tab.FontSize,
        selectionBackground = tab.SelectionColor,
        cursorStyle = tab.CursorStyle.ToLowerInvariant(),
        cursorBlink = tab.CursorBlink,
        theme = tab.Theme,
    };

    private static string CreateLayoutRequest(TerminalTabViewModel tab) =>
        JsonSerializer.Serialize(new
        {
            tabId = GetTerminalId(tab.Id),
            activePaneId = tab.ActivePaneId is Guid active
                ? GetTerminalId(active)
                : null,
            root = SerializeNode(tab.PaneRoot),
        });

    private static object? SerializeNode(HyperTerm.Core.Models.PaneNode? node) =>
        node switch
        {
            HyperTerm.Core.Models.TerminalPaneNode terminal => new
            {
                type = "terminal",
                paneId = GetTerminalId(terminal.PaneId),
            },
            HyperTerm.Core.Models.SplitPaneNode split => new
            {
                type = "split",
                orientation = split.Orientation == HyperTerm.Core.Models.SplitOrientation.Vertical
                    ? "vertical"
                    : "horizontal",
                ratio = split.Ratio,
                first = SerializeNode(split.First),
                second = SerializeNode(split.Second),
            },
            _ => null,
        };

    private static string SerializeTerminalId(Guid tabId) =>
        JsonSerializer.Serialize(GetTerminalId(tabId));

    private static string GetTerminalId(Guid tabId) => tabId.ToString("N");
}
