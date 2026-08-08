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
    private bool settingsKeepPsmuxSessionsOnExit = true;

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
}
