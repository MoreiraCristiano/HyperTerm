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
            diagnostics.LogError(exception, "Failed to prepare a local terminal.");
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
        tab.PtyStarted += OnTabPtyStarted;
        Tabs.Add(tab);
        SelectedTab = tab;
        OnPropertyChanged(nameof(HasOpenTabs));
    }

    private void AttachPsmuxTab(string name, TerminalSessionDefinition definition)
    {
        var tab = new TerminalTabViewModel(
            name,
            definition,
            ptySessionFactory,
            settings.TerminalFontFamily,
            settings.TerminalFontSize,
            settings.TerminalSelectionColor,
            settings.TerminalCursorStyle,
            settings.TerminalCursorBlink,
            CloseTabAsync);
        AttachTab(tab);
    }

    private void NotifyPsmuxSessionsChanged()
    {
        OnPropertyChanged(nameof(HasPsmuxSessions));
        OnPropertyChanged(nameof(HasPsmuxSessionsMessage));
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
            StatusText = $"Failed to stop PowerShell: {exception.Message}";
            return;
        }

        tab.ApplicationCommandRequested -= OnApplicationCommandRequested;
        tab.PtyStarted -= OnTabPtyStarted;
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

    private void OnTabPtyStarted(object? sender, EventArgs eventArgs) =>
        Observe(RefreshStartedPsmuxTabAsync(sender), "refresh psmux sessions");

    private async Task RefreshStartedPsmuxTabAsync(object? sender)
    {
        if (sender is not TerminalTabViewModel { IsPsmux: true } tab ||
            tab.PsmuxSessionName is null)
        {
            return;
        }

        int[] delays = [100, 250, 500, 1000];
        foreach (int delay in delays)
        {
            await Task.Delay(delay);
            await RefreshPsmuxSessionsAsync();
            if (PsmuxSessions.Any(session => session.Name.Equals(
                    tab.PsmuxSessionName,
                    StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            if (Tabs.Contains(tab))
            {
                PsmuxSessions.Add(new PsmuxSessionItemViewModel(
                    new PsmuxSessionInfo(tab.PsmuxSessionName, 1, true)));
            }
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
