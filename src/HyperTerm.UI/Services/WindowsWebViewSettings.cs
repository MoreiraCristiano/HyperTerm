using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Platform;

namespace HyperTerm.UI.Services;

internal static class WindowsWebViewSettings
{
    // ICoreWebView2::get_Settings is the first method after IUnknown.
    private const int GetSettingsVtableIndex = 3;

    // ICoreWebView2Settings3 appends this setter after Settings and Settings2.
    private const int PutBrowserAcceleratorsEnabledVtableIndex = 24;

    private static readonly Guid Settings3InterfaceId =
        new("FDB5AB74-AF33-4854-84F0-0A631DEB5EBA");

    public static bool TryDisableBrowserAccelerators(NativeWebView webView)
    {
        if (!OperatingSystem.IsWindows() ||
            webView.TryGetPlatformHandle() is not IWindowsWebView2PlatformHandle handle ||
            handle.CoreWebView2 == IntPtr.Zero)
        {
            return false;
        }

        IntPtr settings = IntPtr.Zero;
        IntPtr settings3 = IntPtr.Zero;
        try
        {
            IntPtr coreVtable = Marshal.ReadIntPtr(handle.CoreWebView2);
            IntPtr getSettingsMethod = Marshal.ReadIntPtr(
                coreVtable,
                GetSettingsVtableIndex * IntPtr.Size);
            GetSettings getSettings =
                Marshal.GetDelegateForFunctionPointer<GetSettings>(getSettingsMethod);
            if (getSettings(handle.CoreWebView2, out settings) < 0 || settings == IntPtr.Zero)
            {
                return false;
            }

            Guid settings3InterfaceId = Settings3InterfaceId;
            if (Marshal.QueryInterface(
                    settings,
                    in settings3InterfaceId,
                    out settings3) < 0 || settings3 == IntPtr.Zero)
            {
                return false;
            }

            IntPtr settings3Vtable = Marshal.ReadIntPtr(settings3);
            IntPtr putAcceleratorsMethod = Marshal.ReadIntPtr(
                settings3Vtable,
                PutBrowserAcceleratorsEnabledVtableIndex * IntPtr.Size);
            PutBoolean putAccelerators =
                Marshal.GetDelegateForFunctionPointer<PutBoolean>(putAcceleratorsMethod);
            return putAccelerators(settings3, 0) >= 0;
        }
        catch (Exception exception) when (
            exception is ArgumentException or COMException or PlatformNotSupportedException)
        {
            return false;
        }
        finally
        {
            if (settings3 != IntPtr.Zero)
            {
                Marshal.Release(settings3);
            }

            if (settings != IntPtr.Zero)
            {
                Marshal.Release(settings);
            }
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetSettings(IntPtr coreWebView2, out IntPtr settings);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int PutBoolean(IntPtr settings, int value);
}
