using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HyperTerm.Core.Abstractions.Services;
using HyperTerm.Core.Abstractions.Terminal;
using HyperTerm.Core.Entities;
using HyperTerm.Core.Exceptions;
using HyperTerm.Core.Models;

namespace HyperTerm.UI.ViewModels;

public sealed partial class TerminalWorkspaceViewModel(
    ISessionService sessionService,
    ITerminalSessionFactory terminalSessionFactory,
    IPtySessionFactory ptySessionFactory) : ViewModelBase
{
    private ApplicationSettings settings = new();

    public event Action<string>? ApplicationCommandRequested;

    public event Action<string>? SettingsRequested;

    public event Action? SessionsRefreshRequested;

    public ObservableCollection<TerminalTabViewModel> Tabs { get; } = [];

    public string Title => SelectedTab is null
        ? "HyperTerm"
        : $"HyperTerm — {SelectedTab.Title}";

    public bool HasOpenTabs => Tabs.Count > 0;

    [ObservableProperty]
    private TerminalTabViewModel? selectedTab;

    [ObservableProperty]
    private string statusText = "Ready";

    [ObservableProperty]
    private string terminalStatusText = "PowerShell";

    partial void OnSelectedTabChanged(
        TerminalTabViewModel? oldValue,
        TerminalTabViewModel? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.PropertyChanged -= OnSelectedTabPropertyChanged;
        }

        if (newValue is not null)
        {
            newValue.PropertyChanged += OnSelectedTabPropertyChanged;
        }

        foreach (TerminalTabViewModel tab in Tabs)
        {
            tab.IsSelected = ReferenceEquals(tab, newValue);
        }

        if (newValue is not null)
        {
            StatusText = $"Active tab: {newValue.Title}";
            newValue.RequestFocus();
        }

        OnPropertyChanged(nameof(Title));
        CloseSelectedTabCommand.NotifyCanExecuteChanged();
        NextTabCommand.NotifyCanExecuteChanged();
        PreviousTabCommand.NotifyCanExecuteChanged();
    }

    public void ApplySettings(ApplicationSettings value)
    {
        ArgumentNullException.ThrowIfNull(value);
        settings = value;
        TerminalStatusText = $"PowerShell: {Path.GetFileName(settings.PowerShellPath)}";

        foreach (TerminalTabViewModel tab in Tabs)
        {
            tab.UpdateAppearance(
                settings.TerminalFontFamily,
                settings.TerminalFontSize,
                settings.TerminalSelectionColor,
                settings.TerminalCursorStyle,
                settings.TerminalCursorBlink);
        }
    }

    public void SetStatus(string value) => StatusText = value;

    public async Task OpenSessionAsync(SessionListItemViewModel session)
    {
        ArgumentNullException.ThrowIfNull(session);
        TerminalTabViewModel? existingTab = Tabs.FirstOrDefault(
            tab => tab.SessionId == session.Id);

        if (existingTab is not null)
        {
            SelectedTab = existingTab;
            existingTab.RequestFocus();
            StatusText = $"Session ‘{session.Name}’ is already open";
            return;
        }

        try
        {
            Session entity = await sessionService.GetByIdAsync(session.Id)
                ?? throw new KeyNotFoundException($"Session ‘{session.Name}’ was not found.");
            TerminalSessionDefinition definition =
                await terminalSessionFactory.CreateAsync(entity);
            var tab = new TerminalTabViewModel(
                session,
                definition,
                ptySessionFactory,
                settings.TerminalFontFamily,
                settings.TerminalFontSize,
                settings.TerminalSelectionColor,
                settings.TerminalCursorStyle,
                settings.TerminalCursorBlink,
                CloseTabAsync);

            AttachTab(tab);
            StatusText = $"Terminal prepared for ‘{session.Name}’";
        }
        catch (TerminalLaunchException exception)
        {
            StatusText = exception.Message;
            SettingsRequested?.Invoke(exception.Message);
        }
        catch (KeyNotFoundException exception)
        {
            StatusText = exception.Message;
            SessionsRefreshRequested?.Invoke();
        }
    }

    [RelayCommand]
    private async Task OpenLocalTerminalAsync()
    {
        try
        {
            TerminalSessionDefinition definition =
                await terminalSessionFactory.CreateLocalAsync();
            const string title = "PowerShell";
            var tab = new TerminalTabViewModel(
                title,
                definition,
                ptySessionFactory,
                settings.TerminalFontFamily,
                settings.TerminalFontSize,
                settings.TerminalSelectionColor,
                settings.TerminalCursorStyle,
                settings.TerminalCursorBlink,
                CloseTabAsync);

            AttachTab(tab);
            StatusText = $"Local terminal ‘{title}’ opened";
        }
        catch (TerminalLaunchException exception)
        {
            StatusText = exception.Message;
            SettingsRequested?.Invoke(exception.Message);
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelectedTab))]
    private Task CloseSelectedTabAsync() => CloseTabAsync(SelectedTab!);

    [RelayCommand(CanExecute = nameof(HasSelectedTab))]
    private void NextTab() => SelectRelativeTab(1);

    [RelayCommand(CanExecute = nameof(HasSelectedTab))]
    private void PreviousTab() => SelectRelativeTab(-1);

    public async Task SynchronizeTabsAsync(
        IReadOnlyList<SessionListItemViewModel> sessions)
    {
        foreach (TerminalTabViewModel tab in Tabs.ToArray())
        {
            if (tab.IsLocal)
            {
                continue;
            }

            SessionListItemViewModel? session = sessions.FirstOrDefault(
                item => item.Id == tab.SessionId);
            if (session is null)
            {
                await CloseTabAsync(tab);
            }
            else
            {
                tab.UpdateSession(session);
            }
        }
    }

    public async Task ShutdownAsync()
    {
        foreach (TerminalTabViewModel tab in Tabs.ToArray())
        {
            await CloseTabAsync(tab);
        }
    }

    private bool HasSelectedTab() => SelectedTab is not null;

    private void AttachTab(TerminalTabViewModel tab)
    {
        tab.ApplicationCommandRequested += OnApplicationCommandRequested;
        Tabs.Add(tab);
        SelectedTab = tab;
        OnPropertyChanged(nameof(HasOpenTabs));
    }

    private void SelectRelativeTab(int offset)
    {
        if (Tabs.Count == 0)
        {
            return;
        }

        int currentIndex = SelectedTab is null ? -1 : Tabs.IndexOf(SelectedTab);
        int nextIndex = currentIndex < 0
            ? offset > 0 ? 0 : Tabs.Count - 1
            : (currentIndex + offset + Tabs.Count) % Tabs.Count;
        SelectedTab = Tabs[nextIndex];
        SelectedTab.RequestFocus();
    }

    private async Task CloseTabAsync(TerminalTabViewModel tab)
    {
        int closedTabIndex = Tabs.IndexOf(tab);
        if (closedTabIndex < 0)
        {
            return;
        }

        try
        {
            await tab.TerminateAsync();
            await tab.DisposeAsync();
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or Win32Exception)
        {
            StatusText = $"Failed to stop PowerShell: {exception.Message}";
            return;
        }

        tab.ApplicationCommandRequested -= OnApplicationCommandRequested;
        bool wasSelected = ReferenceEquals(SelectedTab, tab);
        Tabs.RemoveAt(closedTabIndex);
        if (wasSelected)
        {
            int nextTabIndex = Math.Min(closedTabIndex, Tabs.Count - 1);
            SelectedTab = nextTabIndex >= 0 ? Tabs[nextTabIndex] : null;
        }

        OnPropertyChanged(nameof(HasOpenTabs));
        StatusText = $"Tab ‘{tab.Title}’ closed";
    }

    private async void OnApplicationCommandRequested(object? sender, string command)
    {
        switch (command)
        {
            case "closeTab" when sender is TerminalTabViewModel tab:
                await CloseTabAsync(tab);
                break;
            case "nextTab":
                NextTab();
                break;
            case "previousTab":
                PreviousTab();
                break;
            default:
                ApplicationCommandRequested?.Invoke(command);
                break;
        }
    }

    private void OnSelectedTabPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(TerminalTabViewModel.Title))
        {
            OnPropertyChanged(nameof(Title));
        }
    }
}
