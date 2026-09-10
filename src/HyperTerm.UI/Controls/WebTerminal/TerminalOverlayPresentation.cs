using Avalonia.Media.Imaging;
using Microsoft.Extensions.Logging;

namespace HyperTerm.UI.Controls;

// Owned by the view; neither native resources nor presentation state belong in view models.
internal sealed class TerminalOverlayPresentation(
    Func<CancellationToken, Task<Bitmap?>> capture,
    Action<bool, Bitmap?> present,
    ILogger logger,
    TimeProvider? timeProvider = null) : IDisposable
{
    private CancellationTokenSource? captureCancellation;
    private Bitmap? preview;
    private Guid? tabId;
    private bool overlayOpen;
    private bool disposed;
    private int generation;

    public async Task UpdateAsync(Guid? activeTabId, bool isOverlayOpen)
    {
        if (disposed || (tabId == activeTabId && overlayOpen == isOverlayOpen))
        {
            return;
        }

        bool tabChanged = tabId != activeTabId;
        tabId = activeTabId;
        overlayOpen = isOverlayOpen;
        int request = ++generation;
        captureCancellation?.Cancel();
        captureCancellation = null;

        if (!isOverlayOpen || activeTabId is null)
        {
            present(activeTabId is not null, null);
            ClearPreview();
            return;
        }

        if (tabChanged)
        {
            present(false, null);
            ClearPreview();
            // A tab changed behind an existing overlay has no visible frame to capture.
            return;
        }

        using var cancellation = new CancellationTokenSource(
            TimeSpan.FromSeconds(1), timeProvider ?? TimeProvider.System);
        captureCancellation = cancellation;
        Bitmap? captured = null;
        try
        {
            captured = await capture(cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            if (request == generation && !disposed)
            {
                logger.LogWarning("Terminal preview capture timed out.");
            }
        }
        catch (Exception exception) when (
            exception is System.Runtime.InteropServices.COMException or InvalidOperationException or ArgumentException or IOException)
        {
            logger.LogWarning(exception, "Unable to capture the terminal preview.");
        }
        finally
        {
            if (ReferenceEquals(captureCancellation, cancellation))
            {
                captureCancellation = null;
            }
        }

        if (disposed || request != generation)
        {
            captured?.Dispose();
            return;
        }

        ClearPreview();
        preview = captured;
        present(false, preview);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        generation++;
        captureCancellation?.Cancel();
        captureCancellation = null;
        present(false, null);
        ClearPreview();
    }

    private void ClearPreview()
    {
        preview?.Dispose();
        preview = null;
    }
}
