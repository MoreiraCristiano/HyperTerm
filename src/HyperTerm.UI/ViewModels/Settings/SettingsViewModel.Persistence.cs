using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HyperTerm.Core.Abstractions.Services;
using HyperTerm.Core.Abstractions.Logging;
using HyperTerm.Core.Abstractions.Settings;
using HyperTerm.Core.Models;
using HyperTerm.Core.Services;
using HyperTerm.UI.Services;
using Microsoft.Extensions.Logging;

namespace HyperTerm.UI.ViewModels;

public sealed partial class SettingsViewModel
{
    [RelayCommand]
    private Task<bool> SaveSettingsAsync() => TrySaveSettingsAsync();

    private async Task<bool> TrySaveSettingsAsync()
    {
        if (!TryBuildTerminalProfiles(out IReadOnlyList<TerminalProfile> profiles))
        {
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
            ThemeOption theme = FindThemeOption(SettingsTheme.Value);
            string cursorStyle = NormalizeCursorStyle(SettingsTerminalCursorStyle);
            applicationSettings = applicationSettings with
            {
                TerminalProfiles = profiles,
                DefaultTerminalProfileId = DefaultTerminalProfileId,
                Theme = theme.Value,
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
            themeService.Apply(applicationSettings.Theme);
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

    private void LoadEditorValues()
    {
        applicationSettings = TerminalProfileCatalog.Normalize(applicationSettings);
        ThemeOption theme = FindThemeOption(applicationSettings.Theme);
        applicationSettings = applicationSettings with { Theme = theme.Value };
        LoadTerminalProfiles(applicationSettings);
        SettingsTheme = theme;
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

    private void LoadSystemFonts(string selectedFontFamily)
    {
        if (SystemFontFamilies.Count == 0)
        {
            foreach (string fontFamily in systemFontService.GetInstalledFontFamilies())
            {
                SystemFontFamilies.Add(fontFamily);
            }
        }

        string normalizedFontFamily = string.IsNullOrWhiteSpace(selectedFontFamily)
            ? "Cascadia Mono"
            : selectedFontFamily.Trim();
        if (!SystemFontFamilies.Contains(normalizedFontFamily))
        {
            SystemFontFamilies.Insert(0, normalizedFontFamily);
        }

        OnPropertyChanged(nameof(SettingsTerminalFontFamilyIndex));
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

    private ThemeOption FindThemeOption(string? value)
    {
        if (string.Equals(value, "Dark", StringComparison.OrdinalIgnoreCase))
        {
            return DefaultTheme;
        }

        return ThemeOptions.FirstOrDefault(option =>
            option.Value.Equals(value, StringComparison.OrdinalIgnoreCase))
            ?? DefaultTheme;
    }
}
