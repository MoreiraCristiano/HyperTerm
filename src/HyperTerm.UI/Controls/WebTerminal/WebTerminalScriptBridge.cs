using System.Text.Json;
using HyperTerm.UI.ViewModels;

namespace HyperTerm.UI.Controls;

internal sealed class WebTerminalScriptBridge(Func<string, Task> invokeScript)
{
    public Task CreateAsync(TerminalTabViewModel tab) =>
        invokeScript($"window.terminalHost.create({CreateRequest(tab)})");

    public Task ConfigureAsync(TerminalTabViewModel tab) =>
        invokeScript($"window.terminalHost.configure({CreateRequest(tab)})");

    public Task DisposeAsync(Guid tabId) =>
        invokeScript($"window.terminalHost.dispose({SerializeTerminalId(tabId)})");

    public Task ActivateAsync(Guid tabId) =>
        invokeScript($"window.terminalHost.activate({SerializeTerminalId(tabId)})");

    public Task FocusAsync(Guid tabId) =>
        invokeScript($"window.terminalHost.focus({SerializeTerminalId(tabId)})");

    public Task WriteAsync(Guid tabId, long token, string output) =>
        invokeScript(
            $"window.terminalHost.write({SerializeTerminalId(tabId)}, {token}, " +
            $"{JsonSerializer.Serialize(output)})");

    private static string CreateRequest(TerminalTabViewModel tab) =>
        JsonSerializer.Serialize(new
        {
            tabId = GetTerminalId(tab.Id),
            options = new
            {
                fontFamily = tab.FontFamily,
                fontSize = tab.FontSize,
                selectionBackground = tab.SelectionColor,
                cursorStyle = tab.CursorStyle.ToLowerInvariant(),
                cursorBlink = tab.CursorBlink,
            },
        });

    private static string SerializeTerminalId(Guid tabId) =>
        JsonSerializer.Serialize(GetTerminalId(tabId));

    private static string GetTerminalId(Guid tabId) => tabId.ToString("N");
}
