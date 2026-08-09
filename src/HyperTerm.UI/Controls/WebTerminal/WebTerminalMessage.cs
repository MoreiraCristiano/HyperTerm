using System.Text.Json;

namespace HyperTerm.UI.Controls;

internal sealed record WebTerminalMessage(
    string Type,
    Guid? TabId = null,
    string? Data = null,
    string? Command = null,
    int Columns = 0,
    int Rows = 0,
    long Token = 0,
    bool Success = true)
{
    private const int MaximumBodyCharacters = 1024 * 1024;
    private const int MaximumInputCharacters = 64 * 1024;
    private const int MaximumCopyCharacters = 1024 * 1024;
    private const int MaximumTerminalDimension = 10_000;
    private static readonly HashSet<string> ApplicationCommands =
    [
        "newSession",
        "openSession",
        "closeTab",
        "nextTab",
        "previousTab",
        "closeWindow",
        "toggleSidebar",
        "settings",
        "searchTerminal",
        "commandPalette",
    ];

    public static bool TryParse(string? body, out WebTerminalMessage? message)
    {
        message = null;
        if (string.IsNullOrWhiteSpace(body) || body.Length > MaximumBodyCharacters)
        {
            return false;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !TryGetString(root, "type", out string type))
            {
                return false;
            }

            if (type == "hostReady")
            {
                message = new WebTerminalMessage(type);
                return true;
            }

            if (!TryGetString(root, "tabId", out string tabIdText) ||
                !Guid.TryParseExact(tabIdText, "N", out Guid tabId))
            {
                return false;
            }

            switch (type)
            {
                case "ready":
                case "resize":
                    if (!TryGetDimension(root, "columns", out int columns) ||
                        !TryGetDimension(root, "rows", out int rows))
                    {
                        return false;
                    }

                    message = new WebTerminalMessage(
                        type,
                        tabId,
                        Columns: columns,
                        Rows: rows);
                    return true;
                case "input":
                case "copy":
                    if (!TryGetString(root, "data", out string data) ||
                        data.Length > (type == "input"
                            ? MaximumInputCharacters
                            : MaximumCopyCharacters))
                    {
                        return false;
                    }

                    message = new WebTerminalMessage(type, tabId, Data: data);
                    return true;
                case "paste":
                    message = new WebTerminalMessage(type, tabId);
                    return true;
                case "applicationCommand":
                    if (!TryGetString(root, "command", out string command) ||
                        !ApplicationCommands.Contains(command))
                    {
                        return false;
                    }

                    message = new WebTerminalMessage(type, tabId, Command: command);
                    return true;
                case "writeComplete":
                    if (!root.TryGetProperty("token", out JsonElement tokenElement) ||
                        !tokenElement.TryGetInt64(out long token) || token <= 0)
                    {
                        return false;
                    }

                    bool success = !root.TryGetProperty("success", out JsonElement successElement) ||
                        successElement.ValueKind == JsonValueKind.True;
                    if (successElement.ValueKind is not (
                            JsonValueKind.Undefined or JsonValueKind.True or JsonValueKind.False))
                    {
                        return false;
                    }

                    message = new WebTerminalMessage(
                        type,
                        tabId,
                        Token: token,
                        Success: success);
                    return true;
                default:
                    return false;
            }
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryGetDimension(
        JsonElement root,
        string propertyName,
        out int value)
    {
        value = 0;
        return root.TryGetProperty(propertyName, out JsonElement element) &&
            element.TryGetInt32(out value) &&
            value is >= 1 and <= MaximumTerminalDimension;
    }

    private static bool TryGetString(
        JsonElement root,
        string propertyName,
        out string value)
    {
        value = string.Empty;
        if (!root.TryGetProperty(propertyName, out JsonElement element) ||
            element.ValueKind != JsonValueKind.String ||
            element.GetString() is not { } parsed)
        {
            return false;
        }

        value = parsed;
        return true;
    }
}
