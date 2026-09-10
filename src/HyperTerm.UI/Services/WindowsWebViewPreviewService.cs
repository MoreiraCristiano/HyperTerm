using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.ExceptionServices;
using System.Runtime.Versioning;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace HyperTerm.UI.Services;

internal sealed class WindowsWebViewPreviewService : IWebViewPreviewService
{
    private bool capturePending;

    public async Task<Bitmap?> CaptureAsync(
        NativeWebView webView, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows() || capturePending ||
            webView.TryGetPlatformHandle() is not IWindowsWebView2PlatformHandle handle ||
            handle.CoreWebView2 == IntPtr.Zero)
        {
            return null;
        }

        byte[] bytes = await CaptureWindowsAsync(handle.CoreWebView2, cancellationToken);
        return await Task.Run(() =>
        {
            using var stream = new MemoryStream(bytes, writable: false);
            return new Bitmap(stream);
        }, cancellationToken);
    }

    [SupportedOSPlatform("windows")]
    private async Task<byte[]> CaptureWindowsAsync(IntPtr core, CancellationToken cancellationToken)
    {
        Marshal.ThrowExceptionForHR(CreateStreamOnHGlobal(IntPtr.Zero, true, out IStream stream));
        capturePending = true;
        var completion = new TaskCompletionSource<CaptureResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var callback = new CaptureCompleted(stream, completion, () => capturePending = false, cancellationToken);
        try
        {
            // ICoreWebView2::CapturePreview follows ExecuteScript (slot 29), including IUnknown.
            IntPtr method = Marshal.ReadIntPtr(Marshal.ReadIntPtr(core), 30 * IntPtr.Size);
            CapturePreview capture = Marshal.GetDelegateForFunctionPointer<CapturePreview>(method);
            int hresult = capture(core, 0, stream, callback);
            if (hresult < 0)
            {
                callback.Invoke(hresult);
            }
        }
        catch (Exception exception) when (exception is COMException or ArgumentException)
        {
            callback.Invoke(exception.HResult);
        }

        // WebView2 owns the callback until completion. Cancellation stops waiting, but must
        // not release its stream while the browser may still be writing to it.
        CaptureResult result = await completion.Task.WaitAsync(cancellationToken);
        if (result.Error is { } error)
        {
            ExceptionDispatchInfo.Capture(error).Throw();
        }

        return result.Bytes!;
    }

    // Native completion may arrive after cancellation. Store failures as data so that
    // an abandoned browser callback can never leave an unobserved faulted task.
    private sealed record CaptureResult(byte[]? Bytes, Exception? Error);

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int CreateStreamOnHGlobal(
        IntPtr memory, [MarshalAs(UnmanagedType.Bool)] bool deleteOnRelease, out IStream stream);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CapturePreview(
        IntPtr core, int format, [MarshalAs(UnmanagedType.Interface)] IStream stream,
        [MarshalAs(UnmanagedType.Interface)] ICaptureCompleted callback);

    [ComVisible(true)]
    [Guid("697E05E9-3D8F-45FA-96F4-8FFE1EDEDAF5")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ICaptureCompleted
    {
        [PreserveSig]
        int Invoke(int errorCode);
    }

    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    [SupportedOSPlatform("windows")]
    private sealed class CaptureCompleted(
        IStream stream, TaskCompletionSource<CaptureResult> completion, Action finished,
        CancellationToken cancellationToken) : ICaptureCompleted
    {
        private bool completed;

        public int Invoke(int errorCode)
        {
            if (completed)
            {
                return 0;
            }

            completed = true;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                Marshal.ThrowExceptionForHR(errorCode);
                stream.Stat(out STATSTG statistics, 1);
                if (statistics.cbSize is <= 0 or > 32 * 1024 * 1024)
                {
                    throw new InvalidOperationException("WebView preview exceeds the image size limit.");
                }

                stream.Seek(0, 0, IntPtr.Zero);
                var bytes = new byte[(int)statistics.cbSize];
                stream.Read(bytes, bytes.Length, IntPtr.Zero);
                completion.TrySetResult(new CaptureResult(bytes, null));
            }
            catch (OperationCanceledException)
            {
                completion.TrySetCanceled(cancellationToken);
            }
            catch (Exception exception) when (exception is COMException or InvalidOperationException)
            {
                completion.TrySetResult(new CaptureResult(null, exception));
            }
            finally
            {
                Marshal.ReleaseComObject(stream);
                finished();
            }

            return 0;
        }
    }
}
