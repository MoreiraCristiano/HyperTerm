using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HyperTerm.Core.Abstractions.Services;
using HyperTerm.Core.Abstractions.Logging;
using HyperTerm.Core.Abstractions.Settings;
using HyperTerm.Core.Models;
using HyperTerm.UI.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HyperTerm.UI.ViewModels;

public sealed partial class SettingsViewModel(
    ISettingsService settingsService,
    IThemeService themeService,
    IExecutableFilePicker executableFilePicker,
    ISessionArchiveService sessionArchiveService,
    ISessionArchiveFilePicker sessionArchiveFilePicker,
    ISystemFontService systemFontService,
    IApplicationLogService? applicationLogService = null,
    ILogInteractionService? logInteractionService = null,
    ILogger<SettingsViewModel>? logger = null) : ViewModelBase, IDisposable
{
    private ApplicationSettings applicationSettings = new();
    private bool windowStateChanged;
    private CancellationTokenSource? logPollingCancellation;
    private readonly ILogger<SettingsViewModel> diagnostics =
        logger ?? NullLogger<SettingsViewModel>.Instance;

    public event Action<ApplicationSettings>? SettingsSaved;
    public event Action? InitialSetupCompleted;
    public event Action? SessionsImported;
    public event Action<string>? StatusRequested;

    public ApplicationSettings Current => applicationSettings;
    public WindowSettings WindowSettings => applicationSettings.Window;
    public bool RequiresInitialPowerShellSelection { get; private set; }
    public IReadOnlyList<string> ThemeOptions { get; } = ["Dark"];
    public IReadOnlyList<string> TerminalCursorStyles { get; } =
        ["Bar", "Block", "Underline"];
    public IReadOnlyList<TerminalSelectionColorOption> TerminalSelectionColors { get; } =
    [
        new("Blue", "#264F78"),
        new("Green", "#275D4E"),
        new("Purple", "#5A3D73"),
        new("Orange", "#754C24"),
        new("Red", "#6E3940"),
        new("Silver", "#5B6068"),
    ];
    public ObservableCollection<string> SystemFontFamilies { get; } = [];
    public bool HasSettingsDataStatus => !string.IsNullOrWhiteSpace(SettingsDataStatus);
    public bool HasLogContent => !string.IsNullOrEmpty(LogContent);
    public bool HasPreviousRunCrash => applicationLogService?.PreviousRunCrashed == true;
    public string LogsDirectory => applicationLogService?.LogsDirectory ?? string.Empty;

    [ObservableProperty]
    private bool isSettingsOpen;

    [ObservableProperty]
    private int selectedSettingsTabIndex;

    [ObservableProperty]
    private bool isPowerShellSetupOpen;

    [ObservableProperty]
    private string? powerShellSetupError;

    [ObservableProperty]
    private string settingsPowerShellPath = "pwsh.exe";

    [ObservableProperty]
    private string settingsTheme = "Dark";

    [ObservableProperty]
    private string settingsTerminalFontFamily = "Cascadia Mono";

    [ObservableProperty]
    private decimal settingsTerminalFontSize = 13;

    [ObservableProperty]
    private TerminalSelectionColorOption settingsTerminalSelectionColor =
        new("Blue", "#264F78");

    [ObservableProperty]
    private string settingsTerminalCursorStyle = "Bar";

    [ObservableProperty]
    private bool settingsTerminalCursorBlink = true;

    [ObservableProperty]
    private bool settingsShowSidebarScrollbar;

    [ObservableProperty]
    private bool settingsCaptureLogs = true;

    [ObservableProperty]
    private string logContent = string.Empty;

    [ObservableProperty]
    private string? logStatus;

    [ObservableProperty]
    private string? settingsError;

    [ObservableProperty]
    private string? settingsDataStatus;

    partial void OnSettingsDataStatusChanged(string? value) =>
        OnPropertyChanged(nameof(HasSettingsDataStatus));

    partial void OnLogContentChanged(string value)
    {
        OnPropertyChanged(nameof(HasLogContent));
        CopyLogsCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsSettingsOpenChanged(bool value) => UpdateLogPolling();

    partial void OnSelectedSettingsTabIndexChanged(int value) => UpdateLogPolling();

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        RequiresInitialPowerShellSelection = !settingsService.Exists();
        try
        {
            applicationSettings = await settingsService.LoadAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(applicationSettings.PowerShellPath))
            {
                applicationSettings = applicationSettings with { PowerShellPath = "pwsh.exe" };
                await settingsService.SaveAsync(applicationSettings, cancellationToken);
            }

            LoadEditorValues();
            themeService.Apply(SettingsTheme);
        }
        catch (Exception exception) when (
            exception is IOException or System.Text.Json.JsonException)
        {
            diagnostics.LogError(exception, "Failed to load application settings.");
            SettingsError = $"Failed to load settings: {exception.Message}";
            IsSettingsOpen = true;
        }
    }

    public void ShowFirstRunSetup()
    {
        if (!RequiresInitialPowerShellSelection)
        {
            return;
        }

        PowerShellSetupError = null;
        IsPowerShellSetupOpen = true;
    }

    public void OpenWithError(string error)
    {
        OpenSettings();
        SettingsError = error;
    }

    public void CaptureWindowState(double width, double height, int x, int y)
    {
        applicationSettings = applicationSettings with
        {
            Window = new WindowSettings
            {
                Width = Math.Max(900, width),
                Height = Math.Max(600, height),
                X = x,
                Y = y,
            },
        };
        windowStateChanged = true;
    }

    public async Task ShutdownAsync()
    {
        StopLogPolling();
        if (windowStateChanged && !RequiresInitialPowerShellSelection)
        {
            await settingsService.SaveAsync(applicationSettings);
            windowStateChanged = false;
        }
    }

    public void Dispose() => StopLogPolling();

    [RelayCommand]
    private void OpenSettings()
    {
        if (IsPowerShellSetupOpen)
        {
            return;
        }

        LoadEditorValues();
        LoadSystemFonts();
        SelectedSettingsTabIndex = 0;
        SettingsError = null;
        SettingsDataStatus = null;
        IsSettingsOpen = true;
    }

    [RelayCommand]
    public void CancelSettings()
    {
        IsSettingsOpen = false;
        SettingsError = null;
        StopLogPolling();
    }

    [RelayCommand]
    private async Task SelectPowerShellAsync()
    {
        string? selectedPath = await PickPowerShellAsync();
        if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            SettingsPowerShellPath = selectedPath;
            SettingsError = null;
        }
    }

    [RelayCommand]
    private async Task UseDefaultPowerShellAsync()
    {
        SettingsPowerShellPath = "pwsh.exe";
        await CompletePowerShellSetupAsync();
    }

    [RelayCommand]
    private async Task ChooseInitialPowerShellAsync()
    {
        PowerShellSetupError = null;
        string? selectedPath = await PickPowerShellAsync();
        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            PowerShellSetupError = SettingsError;
            return;
        }

        SettingsPowerShellPath = selectedPath;
        await CompletePowerShellSetupAsync();
    }

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
                CaptureLogs = SettingsCaptureLogs,
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
        SettingsCaptureLogs = applicationSettings.CaptureLogs;
    }

    private void UpdateLogPolling()
    {
        StopLogPolling();
        if (!IsSettingsOpen || SelectedSettingsTabIndex != 3)
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
