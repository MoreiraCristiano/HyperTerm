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
    [RelayCommand]
    public async Task RefreshPsmuxSessionsAsync(
        CancellationToken cancellationToken = default)
    {
        string? selectedName = SelectedPsmuxSession?.Name;
        IsRefreshingPsmuxSessions = true;
        PsmuxSessionsMessage = "Loading psmux sessions...";
        if (psmuxService is null)
        {
            IsPsmuxAvailable = false;
            PsmuxSessions.Clear();
            SelectedPsmuxSession = null;
            PsmuxSessionsMessage = "psmux integration is unavailable.";
            NotifyPsmuxSessionsChanged();
            IsRefreshingPsmuxSessions = false;
            return;
        }

        try
        {
            PsmuxAvailability availability = await psmuxService.ProbeAsync(cancellationToken);
            IsPsmuxAvailable = availability.IsAvailable;
            if (!availability.IsAvailable)
            {
                PsmuxSessions.Clear();
                SelectedPsmuxSession = null;
                PsmuxSessionsMessage = availability.Error ?? "psmux is unavailable.";
                NotifyPsmuxSessionsChanged();
                return;
            }

            IReadOnlyList<PsmuxSessionInfo> sessions =
                await psmuxService.ListSessionsAsync(cancellationToken);
            PsmuxSessions.Clear();
            foreach (PsmuxSessionInfo session in sessions)
            {
                PsmuxSessions.Add(new PsmuxSessionItemViewModel(session));
            }
            SelectedPsmuxSession = selectedName is null
                ? PsmuxSessions.FirstOrDefault()
                : PsmuxSessions.FirstOrDefault(session => session.Name.Equals(
                      selectedName,
                      StringComparison.OrdinalIgnoreCase)) ?? PsmuxSessions.FirstOrDefault();
            PsmuxSessionsMessage = PsmuxSessions.Count == 0
                ? "No active psmux sessions."
                : null;
            NotifyPsmuxSessionsChanged();
        }
        catch (Exception exception) when (
            exception is TerminalLaunchException or InvalidOperationException)
        {
            diagnostics.LogError(exception, "Failed to refresh psmux sessions.");
            IsPsmuxAvailable = false;
            PsmuxSessionsMessage = exception.Message;
            StatusText = exception.Message;
        }
        finally
        {
            IsRefreshingPsmuxSessions = false;
        }
    }

    [RelayCommand]
    private async Task OpenPsmuxSessionsAsync()
    {
        IsPsmuxSessionsOpen = true;
        await RefreshPsmuxSessionsAsync();
    }

    [RelayCommand]
    private void ClosePsmuxSessions()
    {
        CancelKillPsmuxSession();
        IsPsmuxSessionsOpen = false;
    }

    [RelayCommand]
    private void RequestKillPsmuxSession(PsmuxSessionItemViewModel? session)
    {
        if (session is null || IsKillingPsmuxSession)
        {
            return;
        }

        SelectedPsmuxSession = session;
        PsmuxSessionPendingKill = session;
        PsmuxKillError = null;
        IsPsmuxKillConfirmationOpen = true;
    }

    [RelayCommand]
    private void CancelKillPsmuxSession()
    {
        if (IsKillingPsmuxSession)
        {
            return;
        }

        IsPsmuxKillConfirmationOpen = false;
        PsmuxSessionPendingKill = null;
        PsmuxKillError = null;
    }

    [RelayCommand]
    private async Task ConfirmKillPsmuxSessionAsync()
    {
        PsmuxSessionItemViewModel? session = PsmuxSessionPendingKill;
        if (session is null || psmuxService is null || IsKillingPsmuxSession)
        {
            return;
        }

        IsKillingPsmuxSession = true;
        PsmuxKillError = null;
        try
        {
            await psmuxService.KillSessionAsync(session.Name);

            TerminalTabViewModel[] matchingTabs = Tabs
                .Where(tab => tab.PsmuxSessionName?.Equals(
                    session.Name,
                    StringComparison.OrdinalIgnoreCase) == true)
                .ToArray();
            foreach (TerminalTabViewModel tab in matchingTabs)
            {
                await CloseTabAsync(tab);
            }

            IsPsmuxKillConfirmationOpen = false;
            PsmuxSessionPendingKill = null;
            StatusText = $"psmux session ‘{session.Name}’ ended";
            await RefreshPsmuxSessionsAsync();
        }
        catch (Exception exception) when (
            exception is TerminalLaunchException or InvalidOperationException or ArgumentException)
        {
            diagnostics.LogError(exception, "Failed to end psmux session {SessionName}.", session.Name);
            PsmuxKillError = exception.Message;
            StatusText = exception.Message;
        }
        finally
        {
            IsKillingPsmuxSession = false;
        }
    }

    [RelayCommand]
    private async Task AttachSelectedPsmuxSessionAsync()
    {
        PsmuxSessionItemViewModel? session = SelectedPsmuxSession;
        if (session is null)
        {
            return;
        }

        await OpenPsmuxSessionAsync(session);
        if (Tabs.Any(tab => tab.PsmuxSessionName?.Equals(
                session.Name,
                StringComparison.OrdinalIgnoreCase) == true))
        {
            IsPsmuxSessionsOpen = false;
        }
    }

    [RelayCommand]
    private async Task OpenPsmuxCreateAsync()
    {
        await RefreshPsmuxSessionsAsync();
        if (!IsPsmuxAvailable)
        {
            PsmuxAvailability availability = psmuxService is null
                ? new(false, null, null, "psmux integration is unavailable.")
                : await psmuxService.ProbeAsync();
            StatusText = availability.Error ??
                "psmux.exe was not found in PATH. Install it with ‘winget install psmux’.";
            return;
        }

        PsmuxSessionName = string.Empty;
        PsmuxError = null;
        IsPsmuxDuplicate = false;
        IsPsmuxCreateOpen = true;
    }

    [RelayCommand]
    private void CancelPsmuxCreate()
    {
        IsPsmuxCreateOpen = false;
        IsPsmuxDuplicate = false;
        PsmuxError = null;
    }

    [RelayCommand]
    private async Task ConfirmPsmuxCreateAsync()
    {
        string name = PsmuxSessionName.Trim();
        if (!PsmuxSessionNameValidator.IsValid(name))
        {
            PsmuxError = PsmuxSessionNameValidator.ErrorMessage;
            return;
        }

        PsmuxSessionItemViewModel? existing = PsmuxSessions.FirstOrDefault(
            session => session.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null && !IsPsmuxDuplicate)
        {
            IsPsmuxDuplicate = true;
            PsmuxError = $"Session ‘{existing.Name}’ already exists. Choose Attach existing to continue.";
            return;
        }

        IsPsmuxCreateOpen = false;
        if (existing is not null)
        {
            await OpenPsmuxSessionAsync(existing);
            return;
        }

        try
        {
            TerminalSessionDefinition definition =
                await psmuxService!.CreateSessionDefinitionAsync(name);
            AttachPsmuxTab(name, definition);
            if (!PsmuxSessions.Any(session =>
                    session.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                PsmuxSessions.Add(new PsmuxSessionItemViewModel(
                    new PsmuxSessionInfo(name, 1, true)));
            }

            StatusText = $"psmux session ‘{name}’ opened";
        }
        catch (Exception exception) when (
            exception is TerminalLaunchException or ArgumentException)
        {
            diagnostics.LogError(exception, "Failed to prepare a psmux session.");
            PsmuxError = exception.Message;
            IsPsmuxCreateOpen = true;
        }
    }

    [RelayCommand]
    private async Task OpenPsmuxSessionAsync(PsmuxSessionItemViewModel? session)
    {
        if (session is null || psmuxService is null)
        {
            return;
        }

        TerminalTabViewModel? openTab = Tabs.FirstOrDefault(tab =>
            tab.PsmuxSessionName?.Equals(session.Name, StringComparison.OrdinalIgnoreCase) == true);
        if (openTab is not null)
        {
            SelectedTab = openTab;
            openTab.RequestFocus();
            StatusText = $"psmux session ‘{session.Name}’ is already open";
            return;
        }

        try
        {
            TerminalSessionDefinition definition =
                await psmuxService.CreateAttachDefinitionAsync(session.Name);
            AttachPsmuxTab(session.Name, definition);
            StatusText = $"Attached to psmux session ‘{session.Name}’";
        }
        catch (Exception exception) when (
            exception is TerminalLaunchException or ArgumentException)
        {
            diagnostics.LogError(exception, "Failed to attach a psmux session.");
            StatusText = exception.Message;
            await RefreshPsmuxSessionsAsync();
        }
    }
}
