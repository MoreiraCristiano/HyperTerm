using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HyperTerm.Core.Abstractions.Services;
using HyperTerm.Core.Abstractions.Logging;
using HyperTerm.Core.Abstractions.Settings;
using HyperTerm.Core.Abstractions.Terminal;
using HyperTerm.Core.Models;
using HyperTerm.Core.Services;
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
    ILogger<SettingsViewModel>? logger = null,
    ITerminalProfileResolver? terminalProfileResolver = null) : ViewModelBase, IDisposable
{
    private static readonly ThemeOption DefaultTheme = new(
        "Default Dark",
        "Default Dark",
        "HyperTerm's original dark appearance.",
        "#1E1E1E",
        "#252526",
        "#2D2D30");
    private static readonly ThemeOption DefaultLightTheme = new(
        "Default Light",
        "Default Light",
        "A neutral light appearance inspired by Windows.",
        "#F4F5F7",
        "#E7EAEE",
        "#FAFAFB");
    private static readonly ThemeOption AuroraTheme = new(
        "Aurora",
        "Aurora",
        "A fresh light appearance inspired by clear skies and natural light.",
        "#F5F8FA",
        "#EEF3F6",
        "#FAFCFD");
    private static readonly ThemeOption DarculaTheme = new(
        "Darcula",
        "Darcula",
        "A dark appearance inspired by JetBrains Darcula.",
        "#2B2B2B",
        "#292A2C",
        "#343537");
    private static readonly ThemeOption MintaraTheme = new(
        "Mintara",
        "Mintara",
        "A calm, modern dark appearance with a restrained mint accent.",
        "#161B1A",
        "#1B211F",
        "#202624");
    private static readonly ThemeOption VesperTheme = new(
        "Vesper",
        "Vesper",
        "A sophisticated dark appearance with deep violet undertones.",
        "#17151C",
        "#1D1A24",
        "#221E2A");
    private static readonly ThemeOption AbyssTheme = new(
        "Abyss",
        "Abyss",
        "A deep navy appearance inspired by oceanic and nocturnal tones.",
        "#0D1117",
        "#111820",
        "#161D27");
    private static readonly ThemeOption MintaraLightTheme = new(
        "Mintara Light",
        "Mintara Light",
        "A soft light appearance with Mintara's restrained mint identity.",
        "#F3F7F5",
        "#EAF1EE",
        "#F8FAF9");
    private static readonly ThemeOption VesperLightTheme = new(
        "Vesper Light",
        "Vesper Light",
        "A sophisticated light appearance with Vesper's violet identity.",
        "#F7F5F9",
        "#F0ECF3",
        "#FAF9FB");
    private static readonly ThemeOption AbyssLightTheme = new(
        "Abyss Light",
        "Abyss Light",
        "A cool technical light appearance with Abyss's ocean-blue identity.",
        "#F3F7FA",
        "#EAF1F5",
        "#F8FAFC");
    private ApplicationSettings applicationSettings = new();
    private bool windowStateChanged;
    private CancellationTokenSource? logPollingCancellation;
    private readonly ILogger<SettingsViewModel> diagnostics =
        logger ?? NullLogger<SettingsViewModel>.Instance;

    public event Action<ApplicationSettings>? SettingsSaved;
    public event Action? SessionsImported;
    public event Action<string>? StatusRequested;

    public ApplicationSettings Current => applicationSettings;
    public WindowSettings WindowSettings => applicationSettings.Window;
    public IReadOnlyList<ThemeOption> ThemeOptions { get; } =
    [
        DefaultTheme,
        DarculaTheme,
        MintaraTheme,
        VesperTheme,
        AbyssTheme,
        DefaultLightTheme,
        AuroraTheme,
        MintaraLightTheme,
        VesperLightTheme,
        AbyssLightTheme,
    ];
    public IReadOnlyList<ThemeOption> DarkThemeOptions { get; } =
        [DefaultTheme, DarculaTheme, MintaraTheme, VesperTheme, AbyssTheme];
    public IReadOnlyList<ThemeOption> LightThemeOptions { get; } =
        [DefaultLightTheme, AuroraTheme, MintaraLightTheme, VesperLightTheme, AbyssLightTheme];
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
        new("Follow current theme", "Theme", "Transparent"),
    ];
    public ObservableCollection<string> SystemFontFamilies { get; } = [];
    public ObservableCollection<TerminalProfileItemViewModel> TerminalProfiles { get; } = [];
    public bool HasSelectedTerminalProfile => SelectedTerminalProfile is not null;
    public bool HasNoSelectedTerminalProfile => SelectedTerminalProfile is null;
    public bool HasSettingsDataStatus => !string.IsNullOrWhiteSpace(SettingsDataStatus);
    public bool HasLogContent => !string.IsNullOrEmpty(LogContent);
    public bool HasPreviousRunCrash => applicationLogService?.PreviousRunCrashed == true;
    public string LogsDirectory => applicationLogService?.LogsDirectory ?? string.Empty;

    [ObservableProperty]
    private bool isSettingsOpen;

    [ObservableProperty]
    private string defaultTerminalProfileId = TerminalProfileIds.PowerShell;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedTerminalProfile))]
    [NotifyPropertyChangedFor(nameof(HasNoSelectedTerminalProfile))]
    private TerminalProfileItemViewModel? selectedTerminalProfile;

    [ObservableProperty]
    private ThemeOption settingsTheme = DefaultTheme;

    public ThemeOption? SelectedDarkTheme
    {
        get => DarkThemeOptions.Contains(SettingsTheme) ? SettingsTheme : null;
        set
        {
            if (value is not null && DarkThemeOptions.Contains(value))
            {
                SettingsTheme = value;
            }
        }
    }

    public ThemeOption? SelectedLightTheme
    {
        get => LightThemeOptions.Contains(SettingsTheme) ? SettingsTheme : null;
        set
        {
            if (value is not null && LightThemeOptions.Contains(value))
            {
                SettingsTheme = value;
            }
        }
    }

    [ObservableProperty]
    private string settingsTerminalFontFamily = "Cascadia Mono";

    public int SettingsTerminalFontFamilyIndex
    {
        get => SystemFontFamilies.IndexOf(SettingsTerminalFontFamily);
        set
        {
            if (value >= 0 && value < SystemFontFamilies.Count)
            {
                SettingsTerminalFontFamily = SystemFontFamilies[value];
            }
        }
    }

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
    private bool settingsCloseToSystemTray;

    [ObservableProperty]
    private bool settingsCaptureLogs = true;

    [ObservableProperty]
    private bool settingsPsmuxEnabled;

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

    partial void OnSettingsTerminalFontFamilyChanged(string value) =>
        OnPropertyChanged(nameof(SettingsTerminalFontFamilyIndex));

    partial void OnSettingsThemeChanged(ThemeOption value)
    {
        OnPropertyChanged(nameof(SelectedDarkTheme));
        OnPropertyChanged(nameof(SelectedLightTheme));
    }

    partial void OnLogContentChanged(string value)
    {
        OnPropertyChanged(nameof(HasLogContent));
        CopyLogsCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsSettingsOpenChanged(bool value) => UpdateLogPolling();

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        bool isFirstRun = !settingsService.Exists();
        try
        {
            ApplicationSettings loaded = await settingsService.LoadAsync(cancellationToken);
            if (isFirstRun && loaded.TerminalProfiles.Count == 0)
            {
                loaded = loaded with { PowerShellPath = DetectInitialPowerShellPath() };
            }

            applicationSettings = TerminalProfileCatalog.Normalize(loaded);
            if (isFirstRun)
            {
                await settingsService.SaveAsync(applicationSettings, cancellationToken);
            }

            LoadSystemFonts(applicationSettings.TerminalFontFamily);
            LoadEditorValues();
            themeService.Apply(SettingsTheme.Value);
        }
        catch (Exception exception) when (
            exception is IOException or System.Text.Json.JsonException)
        {
            diagnostics.LogError(exception, "Failed to load application settings.");
            SettingsError = $"Failed to load settings: {exception.Message}";
            IsSettingsOpen = true;
        }
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
        if (windowStateChanged)
        {
            await settingsService.SaveAsync(applicationSettings);
            windowStateChanged = false;
        }
    }

    public void Dispose() => StopLogPolling();

    [RelayCommand]
    private void OpenSettings()
    {
        LoadSystemFonts(applicationSettings.TerminalFontFamily);
        LoadEditorValues();
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

    private string DetectInitialPowerShellPath()
    {
        if (terminalProfileResolver?.TryResolve("pwsh.exe") is not null)
        {
            return "pwsh.exe";
        }

        return "powershell.exe";
    }
}
