using System;
using System.Diagnostics;
using System.IO;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;

namespace HyperTerm.E2E.Tests;

public sealed class DesktopSmokeTests
{
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
}
