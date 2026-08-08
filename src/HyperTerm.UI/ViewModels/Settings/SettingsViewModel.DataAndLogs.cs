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
    private async Task ExportSessionsAsync()
    {
        try
        {
            Stream? destination = await sessionArchiveFilePicker.OpenExportStreamAsync();
            if (destination is null)
            {
                return;
            }

            await using (destination)
            {
                await sessionArchiveService.ExportAsync(destination);
            }

            SettingsError = null;
            SettingsDataStatus = "Sessions and folders exported successfully.";
            StatusRequested?.Invoke("Session archive exported");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            diagnostics.LogError(exception, "Session archive export failed.");
            SettingsDataStatus = null;
            SettingsError = $"Export failed: {exception.Message}";
        }
    }

    [RelayCommand]
    private async Task ImportSessionsAsync()
    {
        try
        {
            Stream? source = await sessionArchiveFilePicker.OpenImportStreamAsync();
            if (source is null)
            {
                return;
            }

            SessionImportResult result;
            await using (source)
            {
                result = await sessionArchiveService.ImportAsync(source);
            }

            SessionsImported?.Invoke();
            SettingsError = null;
            SettingsDataStatus =
                $"Imported {result.ImportedSessions} sessions " +
                $"({result.AddedSessions} new, {result.UpdatedSessions} updated) " +
                $"and {result.AddedFolders} folders.";
            StatusRequested?.Invoke("Session archive imported");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            diagnostics.LogError(exception, "Session archive import failed.");
            SettingsDataStatus = null;
            SettingsError = $"Import failed: {exception.Message}";
        }
    }

    [RelayCommand]
    private async Task RefreshLogsAsync()
    {
        if (applicationLogService is null)
        {
            LogContent = string.Empty;
            return;
        }

        try
        {
            LogContent = await applicationLogService.ReadTailAsync();
            LogStatus = LogContent.Length == 0 ? "No logs captured yet." : null;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            LogStatus = "Could not read logs: " + exception.Message;
        }
    }

    [RelayCommand(CanExecute = nameof(HasLogContent))]
    private async Task CopyLogsAsync()
    {
        if (logInteractionService is null || !HasLogContent)
        {
            return;
        }

        try
        {
            await logInteractionService.CopyAsync(LogContent);
            LogStatus = "Logs copied to clipboard.";
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or IOException)
        {
            LogStatus = "Could not copy logs: " + exception.Message;
        }
    }

    [RelayCommand]
    private async Task OpenLogsFolderAsync()
    {
        if (applicationLogService is null || logInteractionService is null)
        {
            return;
        }

        try
        {
            await logInteractionService.OpenFolderAsync(applicationLogService.LogsDirectory);
            LogStatus = null;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or IOException or
                UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            LogStatus = "Could not open the logs folder: " + exception.Message;
        }
    }
}
