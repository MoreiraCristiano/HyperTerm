using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HyperTerm.Core.Abstractions.Services;
using HyperTerm.Core.Abstractions.Logging;
using HyperTerm.Core.Abstractions.Settings;
using HyperTerm.Core.Models;
using HyperTerm.UI.Services;
using Microsoft.Extensions.Logging;

namespace HyperTerm.UI.ViewModels;

public sealed partial class SettingsViewModel
{
    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        bool wasInitialSetup = RequiresInitialPowerShellSelection;
        if (!await TrySaveSettingsAsync())
        {
            return;
        }

        if (wasInitialSetup)
        {
            RequiresInitialPowerShellSelection = false;
            IsPowerShellSetupOpen = false;
            InitialSetupCompleted?.Invoke();
        }
    }

    private async Task CompletePowerShellSetupAsync()
    {
        PowerShellSetupError = null;
        if (!await TrySaveSettingsAsync())
        {
            PowerShellSetupError = SettingsError;
            return;
        }

        RequiresInitialPowerShellSelection = false;
        IsPowerShellSetupOpen = false;
        InitialSetupCompleted?.Invoke();
    }

    private async Task<bool> TrySaveSettingsAsync()
    {
        string powerShellPath = SettingsPowerShellPath.Trim().Trim('"');
        if (powerShellPath.Length == 0)
        {
            SettingsError = "Enter or select pwsh.exe or powershell.exe.";
            return false;
        }

        if (Path.IsPathRooted(powerShellPath) && !File.Exists(powerShellPath))
        {
            SettingsError = "The selected file does not exist.";
            return false;
        }

        string executableName = Path.GetFileName(powerShellPath);
        if (!executableName.Equals("pwsh.exe", StringComparison.OrdinalIgnoreCase) &&
            !executableName.Equals("powershell.exe", StringComparison.OrdinalIgnoreCase))
        {
            SettingsError = "Enter or select a PowerShell executable named pwsh.exe or powershell.exe.";
            return false;
        }

        try
        {
            string fontFamily = string.IsNullOrWhiteSpace(SettingsTerminalFontFamily)
                ? "Cascadia Mono"
                : SettingsTerminalFontFamily.Trim();
            double fontSize = Math.Clamp((double)SettingsTerminalFontSize, 8, 32);
            TerminalSelectionColorOption selectionColor =
                FindSelectionColorOption(SettingsTerminalSelectionColor.Value);
            string cursorStyle = NormalizeCursorStyle(SettingsTerminalCursorStyle);
            applicationSettings = applicationSettings with
            {
                PowerShellPath = powerShellPath,
                Theme = "Dark",
                TerminalFontFamily = fontFamily,
                TerminalFontSize = fontSize,
                TerminalSelectionColor = selectionColor.Value,
                TerminalCursorStyle = cursorStyle,
                TerminalCursorBlink = SettingsTerminalCursorBlink,
                ShowSidebarScrollbar = SettingsShowSidebarScrollbar,
                CloseToSystemTray = SettingsCloseToSystemTray,
                CaptureLogs = SettingsCaptureLogs,
                KeepPsmuxSessionsOnExit = SettingsKeepPsmuxSessionsOnExit,
            };
            await settingsService.SaveAsync(applicationSettings);
            applicationLogService?.Configure(applicationSettings.CaptureLogs);
            LoadEditorValues();
            themeService.Apply("Dark");
            SettingsError = null;
            IsSettingsOpen = false;
            SettingsSaved?.Invoke(applicationSettings);
            StatusRequested?.Invoke("Settings saved");
            return true;
        }
        catch (IOException exception)
        {
            diagnostics.LogError(exception, "Failed to save application settings.");
            SettingsError = $"Failed to save settings: {exception.Message}";
            return false;
        }
    }

    private async Task<string?> PickPowerShellAsync()
    {
        try
        {
            return await executableFilePicker.PickPowerShellAsync();
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or IOException)
        {
            diagnostics.LogWarning(exception, "PowerShell executable selection failed.");
            SettingsError = $"Could not select the executable: {exception.Message}";
            return null;
        }
    }

    private void LoadEditorValues()
    {
        SettingsPowerShellPath = applicationSettings.PowerShellPath;
        SettingsTheme = "Dark";
        SettingsTerminalFontFamily = applicationSettings.TerminalFontFamily;
        SettingsTerminalFontSize = (decimal)applicationSettings.TerminalFontSize;
        SettingsTerminalSelectionColor =
            FindSelectionColorOption(applicationSettings.TerminalSelectionColor);
        SettingsTerminalCursorStyle = NormalizeCursorStyle(
            applicationSettings.TerminalCursorStyle);
        SettingsTerminalCursorBlink = applicationSettings.TerminalCursorBlink;
        SettingsShowSidebarScrollbar = applicationSettings.ShowSidebarScrollbar;
        SettingsCloseToSystemTray = applicationSettings.CloseToSystemTray;
        SettingsCaptureLogs = applicationSettings.CaptureLogs;
        SettingsKeepPsmuxSessionsOnExit = applicationSettings.KeepPsmuxSessionsOnExit;
    }

    private void UpdateLogPolling()
    {
        StopLogPolling();
        if (!IsSettingsOpen)
        {
            return;
        }

        logPollingCancellation = new CancellationTokenSource();
        _ = PollLogsAsync(logPollingCancellation.Token);
    }

    private void StopLogPolling()
    {
        logPollingCancellation?.Cancel();
        logPollingCancellation?.Dispose();
        logPollingCancellation = null;
    }

    private async Task PollLogsAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await RefreshLogsAsync();
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void LoadSystemFonts()
    {
        if (SystemFontFamilies.Count > 0)
        {
            return;
        }

        foreach (string fontFamily in systemFontService.GetInstalledFontFamilies())
        {
            SystemFontFamilies.Add(fontFamily);
        }

        if (!SystemFontFamilies.Contains(SettingsTerminalFontFamily))
        {
            SystemFontFamilies.Insert(0, SettingsTerminalFontFamily);
        }
    }

    private static string NormalizeCursorStyle(string? cursorStyle) =>
        cursorStyle?.Trim().ToLowerInvariant() switch
        {
            "block" => "Block",
            "underline" => "Underline",
            _ => "Bar",
        };

    private TerminalSelectionColorOption FindSelectionColorOption(string? value) =>
        TerminalSelectionColors.FirstOrDefault(option =>
            option.Value.Equals(value, StringComparison.OrdinalIgnoreCase))
        ?? TerminalSelectionColors[0];
}
