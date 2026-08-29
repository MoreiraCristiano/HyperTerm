using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Tools;
using FlaUI.UIA3;

namespace HyperTerm.E2E.Tests;

public sealed class DesktopSmokeTests
{
    [Fact]
    [Trait("Category", "E2E")]
    public void Published_application_exposes_native_windows_snap_prerequisites()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("HYPERTERM_RUN_E2E"),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        string executable = Environment.GetEnvironmentVariable("HYPERTERM_E2E_APP")
            ?? throw new InvalidOperationException("HYPERTERM_E2E_APP is required for E2E tests.");
        if (!File.Exists(executable))
        {
            throw new FileNotFoundException("Published HyperTerm executable was not found.", executable);
        }

        string dataRoot = Path.Combine(
            Path.GetTempPath(), "HyperTerm.E2E", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataRoot);
        Application? application = null;
        try
        {
            var startInfo = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(executable),
            };
            startInfo.EnvironmentVariables["HYPERTERM_TEST_MODE"] = "1";
            startInfo.EnvironmentVariables["HYPERTERM_DATA_ROOT"] = dataRoot;

            application = Application.Launch(startInfo);
            using var automation = new UIA3Automation();
            Window window = application.GetMainWindow(automation, TimeSpan.FromSeconds(30))
                ?? throw new InvalidOperationException("HyperTerm main window did not appear.");
            Assert.True(Retry.WhileFalse(
                () => window.Patterns.Window.Pattern.WindowVisualState.Value ==
                      WindowVisualState.Maximized,
                TimeSpan.FromSeconds(5),
                ignoreException: true).Success);
            IntPtr windowHandle = window.Properties.NativeWindowHandle.Value;
            long windowStyle = GetWindowLongPtr(windowHandle, WindowStyleIndex).ToInt64();
            Assert.NotEqual(0, windowStyle & ResizableFrameStyle);
            Assert.NotEqual(0, windowStyle & MaximizeBoxStyle);
            Assert.True(window.Patterns.Window.Pattern.CanMaximize.Value);

            var maximizedBounds = window.BoundingRectangle;
            window.Patterns.Window.Pattern.SetWindowVisualState(WindowVisualState.Normal);
            Assert.True(Retry.WhileFalse(
                () => window.Patterns.Window.Pattern.WindowVisualState.Value ==
                      WindowVisualState.Normal,
                TimeSpan.FromSeconds(5),
                ignoreException: true).Success);

            var transform = window.Patterns.Transform.Pattern;
            Assert.True(transform.CanResize.Value);
            double halfScreenWidth = maximizedBounds.Width / 2d;
            double targetHeight = Math.Min(700, maximizedBounds.Height);
            transform.Resize(halfScreenWidth, targetHeight);
            RetryResult<bool> halfScreenResize = Retry.WhileFalse(
                () => Math.Abs(window.BoundingRectangle.Width - halfScreenWidth) <= 16,
                TimeSpan.FromSeconds(5),
                ignoreException: true);
            Assert.True(
                halfScreenResize.Success,
                $"Expected a {halfScreenWidth}px-wide window; actual {window.BoundingRectangle}.");
            window.Close();
        }
        finally
        {
            if (application is not null)
            {
                try
                {
                    if (!application.HasExited)
                    {
                        application.Kill();
                    }
                }
                catch (InvalidOperationException)
                {
                }
                finally
                {
                    application.Dispose();
                }
            }

            if (Directory.Exists(dataRoot))
            {
                Directory.Delete(dataRoot, recursive: true);
            }
        }
    }

    [Fact]
    [Trait("Category", "E2E")]
    public void Published_application_starts_with_isolated_data_and_exposes_main_window()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("HYPERTERM_RUN_E2E"),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        string executable = Environment.GetEnvironmentVariable("HYPERTERM_E2E_APP")
            ?? throw new InvalidOperationException("HYPERTERM_E2E_APP is required for E2E tests.");
        if (!File.Exists(executable))
        {
            throw new FileNotFoundException("Published HyperTerm executable was not found.", executable);
        }

        string dataRoot = Path.Combine(
            Path.GetTempPath(), "HyperTerm.E2E", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataRoot);
        Application? application = null;
        try
        {
            var startInfo = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(executable),
            };
            startInfo.EnvironmentVariables["HYPERTERM_TEST_MODE"] = "1";
            startInfo.EnvironmentVariables["HYPERTERM_DATA_ROOT"] = dataRoot;

            application = Application.Launch(startInfo);
            using var automation = new UIA3Automation();
            Window window = application.GetMainWindow(automation, TimeSpan.FromSeconds(30))
                ?? throw new InvalidOperationException("HyperTerm main window did not appear.");

            Assert.Contains("HyperTerm", window.Title, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("MainWindow", window.AutomationId);
            window.Close();
        }
        finally
        {
            if (application is not null)
            {
                try
                {
                    if (!application.HasExited)
                    {
                        application.Kill();
                    }
                }
                catch (InvalidOperationException)
                {
                }
                finally
                {
                    application.Dispose();
                }
            }

            if (Directory.Exists(dataRoot))
            {
                Directory.Delete(dataRoot, recursive: true);
            }
        }
    }

    [Fact]
    [Trait("Category", "E2E")]
    public void Second_launch_restores_existing_window_from_system_tray()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("HYPERTERM_RUN_E2E"),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        string executable = Environment.GetEnvironmentVariable("HYPERTERM_E2E_APP")
            ?? throw new InvalidOperationException("HYPERTERM_E2E_APP is required for E2E tests.");
        string dataRoot = Path.Combine(
            Path.GetTempPath(), "HyperTerm.E2E", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataRoot);
        File.WriteAllText(
            Path.Combine(dataRoot, "settings.json"),
            "{\"PowerShellPath\":\"pwsh.exe\",\"CloseToSystemTray\":true}");

        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(executable),
        };
        startInfo.EnvironmentVariables["HYPERTERM_TEST_MODE"] = "1";
        startInfo.EnvironmentVariables["HYPERTERM_DATA_ROOT"] = dataRoot;

        Application? primary = null;
        try
        {
            primary = Application.Launch(startInfo);
            int primaryProcessId = primary.ProcessId;
            using var automation = new UIA3Automation();
            Window window = primary.GetMainWindow(automation, TimeSpan.FromSeconds(30))
                ?? throw new InvalidOperationException("HyperTerm main window did not appear.");

            window.Close();
            Assert.False(primary.HasExited);
            Assert.True(Retry.WhileFalse(
                () => window.IsOffscreen,
                TimeSpan.FromSeconds(5),
                ignoreException: true).Success);

            using Process secondary = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Second HyperTerm process did not start.");
            Assert.True(secondary.WaitForExit(5000));
            Assert.Equal(0, secondary.ExitCode);

            Window restoredWindow = primary.GetMainWindow(automation, TimeSpan.FromSeconds(10))
                ?? throw new InvalidOperationException("Existing HyperTerm window was not restored.");
            Assert.True(Retry.WhileFalse(
                () => !restoredWindow.IsOffscreen,
                TimeSpan.FromSeconds(5),
                ignoreException: true).Success);
            Assert.Equal(primaryProcessId, primary.ProcessId);
        }
        finally
        {
            if (primary is not null)
            {
                try
                {
                    if (!primary.HasExited)
                    {
                        primary.Kill();
                    }
                }
                catch (InvalidOperationException)
                {
                }
                finally
                {
                    primary.Dispose();
                }
            }

            if (Directory.Exists(dataRoot))
            {
                Directory.Delete(dataRoot, recursive: true);
            }
        }
    }

    private const int WindowStyleIndex = -16;
    private const long ResizableFrameStyle = 0x00040000;
    private const long MaximizeBoxStyle = 0x00010000;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr windowHandle, int index);
}
