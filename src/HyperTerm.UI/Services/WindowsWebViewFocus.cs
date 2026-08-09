using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Platform;

namespace HyperTerm.UI.Services;

internal static class WindowsWebViewFocus
{
    // ICoreWebView2Controller is an append-only COM interface. MoveFocus is
    // the tenth controller method, after the three IUnknown entries.
    private const int MoveFocusVtableIndex = 12;

    public static bool TryMoveFocus(NativeWebView webView)
    {
        if (!OperatingSystem.IsWindows() ||
            webView.TryGetPlatformHandle() is not IWindowsWebView2PlatformHandle handle ||
            handle.CoreWebView2Controller == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            IntPtr vtable = Marshal.ReadIntPtr(handle.CoreWebView2Controller);
            IntPtr method = Marshal.ReadIntPtr(
                vtable,
                MoveFocusVtableIndex * IntPtr.Size);
            MoveFocus moveFocus = Marshal.GetDelegateForFunctionPointer<MoveFocus>(method);
            return moveFocus(
                       handle.CoreWebView2Controller,
                       CoreWebView2MoveFocusReason.Programmatic) >= 0;
        }
        catch (Exception exception) when (
            exception is ArgumentException or COMException or PlatformNotSupportedException)
        {
            return false;
        }
    }

    public static bool TryReleaseFocus(TopLevel topLevel)
    {
        if (!OperatingSystem.IsWindows() ||
            topLevel.TryGetPlatformHandle() is not { } handle ||
            !string.Equals(
                handle.HandleDescriptor,
                "HWND",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            IntPtr user32 = NativeLibrary.Load("user32.dll");
            try
            {
                IntPtr method = NativeLibrary.GetExport(user32, "SetFocus");
                SetWindowFocus setFocus =
                    Marshal.GetDelegateForFunctionPointer<SetWindowFocus>(method);
                setFocus(handle.Handle);
            }
            finally
            {
                NativeLibrary.Free(user32);
            }

            return true;
        }
        catch (Exception exception) when (
            exception is DllNotFoundException or EntryPointNotFoundException or PlatformNotSupportedException)
        {
            return false;
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr SetWindowFocus(IntPtr windowHandle);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int MoveFocus(
        IntPtr controller,
        CoreWebView2MoveFocusReason reason);

    private enum CoreWebView2MoveFocusReason
    {
        Programmatic = 0,
    }
}
