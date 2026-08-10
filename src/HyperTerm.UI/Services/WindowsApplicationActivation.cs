using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace HyperTerm.UI.Services;

internal static class WindowsApplicationActivation
{
    private const uint MessageBoxIconWarning = 0x00000030;

    public static bool TryAllowForegroundActivation(int processId)
    {
        if (!OperatingSystem.IsWindows() || processId <= 0)
        {
            return false;
        }

        return InvokeUser32<AllowSetForegroundWindow, bool>(
            "AllowSetForegroundWindow",
            method => method(processId));
    }

    public static bool TryBringToForeground(TopLevel topLevel)
    {
        ArgumentNullException.ThrowIfNull(topLevel);
        if (!OperatingSystem.IsWindows() ||
            topLevel.TryGetPlatformHandle() is not { } handle ||
            !string.Equals(handle.HandleDescriptor, "HWND", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return InvokeUser32<SetForegroundWindow, bool>(
            "SetForegroundWindow",
            method => method(handle.Handle));
    }

    public static void ShowActivationFailure()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        InvokeUser32<MessageBox, int>(
            "MessageBoxW",
            method => method(
                IntPtr.Zero,
                "HyperTerm is already running but did not respond. Try opening it from " +
                "the system tray, or end the existing process in Task Manager.",
                "HyperTerm",
                MessageBoxIconWarning));
    }

    private static TResult InvokeUser32<TDelegate, TResult>(
        string exportName,
        Func<TDelegate, TResult> invoke)
        where TDelegate : Delegate
    {
        try
        {
            IntPtr user32 = NativeLibrary.Load("user32.dll");
            try
            {
                IntPtr methodAddress = NativeLibrary.GetExport(user32, exportName);
                TDelegate method = Marshal.GetDelegateForFunctionPointer<TDelegate>(methodAddress);
                return invoke(method);
            }
            finally
            {
                NativeLibrary.Free(user32);
            }
        }
        catch (Exception exception) when (
            exception is DllNotFoundException or EntryPointNotFoundException or
                PlatformNotSupportedException)
        {
            return default!;
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private delegate bool AllowSetForegroundWindow(int processId);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private delegate bool SetForegroundWindow(IntPtr windowHandle);

    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)]
    private delegate int MessageBox(
        IntPtr windowHandle,
        string text,
        string caption,
        uint type);
}
