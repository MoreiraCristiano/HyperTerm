using Avalonia.Controls;
using Avalonia.Media.Imaging;

namespace HyperTerm.UI.Services;

internal interface IWebViewPreviewService
{
    // Cancellation must stop waiting promptly without releasing resources still owned by WebView2.
    // The caller owns the returned bitmap. Implementations must bound concurrent native captures.
    Task<Bitmap?> CaptureAsync(NativeWebView webView, CancellationToken cancellationToken);
}
