using Avalonia.Input.Platform;
using HyperTerm.UI.ViewModels;

namespace HyperTerm.UI.Controls;

internal sealed class WebTerminalClipboard(Func<IClipboard?> getClipboard)
{
    public async Task CopyAsync(TerminalTabViewModel tab, string text)
    {
        if (text.Length == 0)
        {
            return;
        }

        try
        {
            IClipboard? clipboard = getClipboard();
            if (clipboard is null)
            {
                tab.ReportCopyFailed("Windows clipboard is unavailable");
                return;
            }

            await clipboard.SetTextAsync(text);
            tab.ReportTextCopied(text.Length);
        }
        catch (Exception exception)
        {
            tab.ReportCopyFailed(exception.Message);
        }
    }

    public async Task PasteAsync(TerminalTabViewModel tab)
    {
        try
        {
            IClipboard? clipboard = getClipboard();
            if (clipboard is null)
            {
                tab.ReportPasteFailed("Windows clipboard is unavailable");
                return;
            }

            string? text = await clipboard.TryGetTextAsync();
            if (!string.IsNullOrEmpty(text))
            {
                await tab.WritePtyAsync(text);
            }
        }
        catch (Exception exception)
        {
            tab.ReportPasteFailed(exception.Message);
        }
    }
}
