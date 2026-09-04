using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HyperTerm.Core.Abstractions.Services;
using HyperTerm.Core.Abstractions.Terminal;
using HyperTerm.Core.Entities;
using HyperTerm.Core.Exceptions;
using HyperTerm.Core.Models;
using Microsoft.Extensions.Logging;

namespace HyperTerm.UI.ViewModels;

public sealed partial class TerminalWorkspaceViewModel
{
    public async Task OpenSessionAsync(SessionListItemViewModel session)
    {
        ArgumentNullException.ThrowIfNull(session);

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
                settings.Theme,
                CloseTabAsync);

            AttachTab(tab);
            StatusText = $"Terminal prepared for ‘{session.Name}’";
        }
        catch (TerminalLaunchException exception)
        {
            diagnostics.LogError(exception, "Failed to prepare an SSH terminal.");
            StatusText = exception.Message;
            SettingsRequested?.Invoke(exception.Message);
        }
        catch (KeyNotFoundException exception)
        {
            diagnostics.LogWarning(exception, "A requested saved session was not found.");
            StatusText = exception.Message;
            SessionsRefreshRequested?.Invoke();
        }
    }

    [RelayCommand]
    private async Task OpenLocalTerminalAsync()
    {
        await OpenLocalTerminalDefinitionAsync(
            () => terminalSessionFactory.CreateLocalAsync());
    }

    [RelayCommand]
    private async Task OpenTerminalProfileAsync(TerminalLaunchProfileViewModel? profile)
    {
        if (profile is null || !profile.IsAvailable)
        {
            return;
        }

        await OpenLocalTerminalDefinitionAsync(
            () => terminalSessionFactory.CreateProfileAsync(profile.Id));
    }

    private async Task OpenLocalTerminalDefinitionAsync(
        Func<Task<TerminalSessionDefinition>> createDefinition)
    {
        try
        {
            TerminalSessionDefinition definition = await createDefinition();
            string title = definition.DisplayName ?? "Terminal";
            var tab = new TerminalTabViewModel(
                title,
                definition,
                ptySessionFactory,
                settings.TerminalFontFamily,
                settings.TerminalFontSize,
                settings.TerminalSelectionColor,
                settings.TerminalCursorStyle,
                settings.TerminalCursorBlink,
                settings.Theme,
                CloseTabAsync);

            AttachTab(tab);
            StatusText = $"Local terminal ‘{title}’ opened";
        }
        catch (Exception exception) when (
            exception is TerminalLaunchException or KeyNotFoundException)
        {
            diagnostics.LogError(exception, "Failed to prepare a local terminal.");
            StatusText = exception.Message;
            SettingsRequested?.Invoke(exception.Message);
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelectedTab))]
    private Task CloseSelectedTabAsync() => CloseTabAsync(SelectedTab!);

    [RelayCommand(CanExecute = nameof(HasSelectedTab))]
    private void SplitRight() => SplitSelectionRequested?.Invoke(SplitOrientation.Vertical);

    [RelayCommand(CanExecute = nameof(HasSelectedTab))]
    private void SplitDown() => SplitSelectionRequested?.Invoke(SplitOrientation.Horizontal);

    [RelayCommand(CanExecute = nameof(HasSelectedTab))]
    private async Task ClosePaneAsync() =>
        _ = await SelectedTab!.CloseActivePaneAsync();

    [RelayCommand(CanExecute = nameof(HasSelectedTab))]
    private void FocusNextPane() => SelectedTab!.FocusNextPane();

    [RelayCommand(CanExecute = nameof(HasSelectedTab))]
    private void FocusPreviousPane() => SelectedTab!.FocusPreviousPane();

    [RelayCommand(CanExecute = nameof(HasSelectedTab))]
    private void FocusLeftPane() => SelectedTab!.FocusLeftPane();

    [RelayCommand(CanExecute = nameof(HasSelectedTab))]
    private void FocusRightPane() => SelectedTab!.FocusRightPane();

    [RelayCommand(CanExecute = nameof(HasSelectedTab))]
    private void FocusUpPane() => SelectedTab!.FocusUpPane();

    [RelayCommand(CanExecute = nameof(HasSelectedTab))]
    private void FocusDownPane() => SelectedTab!.FocusDownPane();

    public Task SplitWithTerminalProfileAsync(
        SplitOrientation orientation,
        TerminalLaunchProfileViewModel? profile)
    {
        if (profile is null || !profile.IsAvailable)
        {
            return Task.CompletedTask;
        }

        return SplitSelectedPaneAsync(
            orientation,
            () => terminalSessionFactory.CreateProfileAsync(profile.Id),
            profile.Name,
            refreshSessionsOnNotFound: false);
    }

    public Task SplitWithSessionAsync(
        SplitOrientation orientation,
        SessionListItemViewModel? session)
    {
        if (session is null)
        {
            return Task.CompletedTask;
        }

        return SplitSelectedPaneAsync(
            orientation,
            async () =>
            {
                Session entity = await sessionService.GetByIdAsync(session.Id)
                    ?? throw new KeyNotFoundException(
                        $"Session ‘{session.Name}’ was not found.");
                return await terminalSessionFactory.CreateAsync(entity);
            },
            session.Name,
            refreshSessionsOnNotFound: true);
    }

    private async Task SplitSelectedPaneAsync(
        SplitOrientation orientation,
        Func<Task<TerminalSessionDefinition>> createDefinition,
        string targetName,
        bool refreshSessionsOnNotFound)
    {
        TerminalTabViewModel? tab = SelectedTab;
        if (tab?.ActivePane is null)
        {
            return;
        }

        try
        {
            TerminalSessionDefinition definition = await createDefinition();
            if (!Tabs.Contains(tab))
            {
                return;
            }

            if (tab.SplitActivePane(orientation, definition) is not null)
            {
                tab.RequestFocus();
                StatusText = orientation == SplitOrientation.Vertical
                    ? $"‘{targetName}’ split right"
                    : $"‘{targetName}’ split down";
            }
        }
        catch (TerminalLaunchException exception)
        {
            diagnostics.LogError(exception, "Failed to prepare a split terminal.");
            StatusText = exception.Message;
            SettingsRequested?.Invoke(exception.Message);
        }
        catch (KeyNotFoundException exception)
        {
            diagnostics.LogWarning(exception, "A split target was not found.");
            StatusText = exception.Message;
            if (refreshSessionsOnNotFound)
            {
                SessionsRefreshRequested?.Invoke();
            }
            else
            {
                SettingsRequested?.Invoke(exception.Message);
            }
        }
    }

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

    public void MoveTab(
        TerminalTabViewModel tab,
        TerminalTabViewModel targetTab,
        bool insertAfter)
    {
        int sourceIndex = Tabs.IndexOf(tab);
        int targetIndex = Tabs.IndexOf(targetTab);
        if (sourceIndex < 0 || targetIndex < 0)
        {
            return;
        }

        int destinationIndex = targetIndex + (insertAfter ? 1 : 0);
        if (sourceIndex < destinationIndex)
        {
            destinationIndex--;
        }

        destinationIndex = Math.Clamp(destinationIndex, 0, Tabs.Count - 1);
        if (sourceIndex != destinationIndex)
        {
            Tabs.Move(sourceIndex, destinationIndex);
        }
    }

    public void RestoreTabAfterDrag(TerminalTabViewModel tab)
    {
        ArgumentNullException.ThrowIfNull(tab);
        if (!Tabs.Contains(tab))
        {
            return;
        }

        if (ReferenceEquals(SelectedTab, tab))
        {
            tab.RequestFocus();
        }
        else
        {
            SelectedTab = tab;
        }
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
            await tab.DisposeAsync();
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or Win32Exception)
        {
            diagnostics.LogError(exception, "Failed to stop a terminal.");
            StatusText = $"Failed to stop terminal: {exception.Message}";
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

    private void OnApplicationCommandRequested(object? sender, string command) =>
        Observe(
            HandleApplicationCommandAsync(sender, command),
            "execute terminal command");

    private async Task HandleApplicationCommandAsync(object? sender, string command)
    {
        switch (command)
        {
            case "closeTab" when sender is TerminalTabViewModel tab:
                await CloseTabAsync(tab);
                break;
            case "closePane":
                await ClosePaneAsync();
                break;
            case "splitRight":
                SplitRight();
                break;
            case "splitDown":
                SplitDown();
                break;
            case "focusNextPane":
                FocusNextPane();
                break;
            case "focusPreviousPane":
                FocusPreviousPane();
                break;
            case "focusLeftPane":
                FocusLeftPane();
                break;
            case "focusRightPane":
                FocusRightPane();
                break;
            case "focusUpPane":
                FocusUpPane();
                break;
            case "focusDownPane":
                FocusDownPane();
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

    private void Observe(Task operation, string operationName) =>
        _ = ObserveAsync(operation, operationName);

    private async Task ObserveAsync(Task operation, string operationName)
    {
        try
        {
            await operation;
        }
        catch (Exception exception)
        {
            diagnostics.LogError(exception, "Failed to {Operation}.", operationName);
            StatusText = $"Failed to {operationName}: {exception.Message}";
        }
    }
}
