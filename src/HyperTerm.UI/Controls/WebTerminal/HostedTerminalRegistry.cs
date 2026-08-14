using System.Collections.Concurrent;
using System.Collections.Specialized;
using HyperTerm.UI.ViewModels;

namespace HyperTerm.UI.Controls;

internal sealed class HostedTerminalRegistry
{
    private readonly ConcurrentDictionary<Guid, HostedTerminal> terminals = new();
    private readonly ITerminalSurface scriptBridge;
    private readonly Func<TerminalTabViewModel?> getActiveTab;
    private readonly Func<bool> isHostReady;
    private readonly Action<TerminalTabViewModel> requestFocus;
    private readonly TerminalOutputCoordinator outputCoordinator;
    private INotifyCollectionChanged? observedCollection;
    private IEnumerable<TerminalTabViewModel>? observedTabs;

    public HostedTerminalRegistry(
        ITerminalSurface scriptBridge,
        Func<TerminalTabViewModel?> getActiveTab,
        Func<bool> isHostReady,
        Action<TerminalTabViewModel> requestFocus,
        Action scheduleOutputFlush)
    {
        this.scriptBridge = scriptBridge;
        this.getActiveTab = getActiveTab;
        this.isHostReady = isHostReady;
        this.requestFocus = requestFocus;
        outputCoordinator = new TerminalOutputCoordinator(
            () => terminals.Values.ToArray(),
            GetActiveHostedTerminal,
            (hosted, output, token) =>
                scriptBridge.WriteAsync(hosted.Pane.PaneId, token, output),
            scheduleOutputFlush);
    }

    public void Observe(IEnumerable<TerminalTabViewModel>? tabs)
    {
        if (ReferenceEquals(observedCollection, tabs))
        {
            Synchronize(tabs);
            return;
        }

        StopObserving();
        observedTabs = tabs;
        observedCollection = tabs as INotifyCollectionChanged;
        if (observedCollection is not null)
        {
            observedCollection.CollectionChanged += OnCollectionChanged;
        }

        Synchronize(tabs);
    }

    public void StopObserving()
    {
        if (observedCollection is not null)
        {
            observedCollection.CollectionChanged -= OnCollectionChanged;
            observedCollection = null;
        }

        observedTabs = null;

        foreach (HostedTerminal hosted in terminals.Values.ToArray())
        {
            Remove(hosted.Tab);
        }
    }

    public bool TryGet(Guid paneId, out HostedTerminal hosted) =>
        terminals.TryGetValue(paneId, out hosted!);

    public async Task CreateExistingAsync()
    {
        HostedTerminal[] existing = terminals.Values
            .OrderByDescending(hosted => ReferenceEquals(hosted.Tab, getActiveTab()))
            .ToArray();
        foreach (HostedTerminal hosted in existing)
        {
            await CreateAsync(hosted);
        }
    }

    public async Task ActivateAsync(TerminalTabViewModel? tab)
    {
        if (!isHostReady() || tab is null ||
            tab.ActivePaneId is not Guid activePaneId ||
            !terminals.TryGetValue(activePaneId, out HostedTerminal? hosted))
        {
            return;
        }

        if (!hosted.Created)
        {
            await CreateAsync(hosted);
        }

        if (hosted.Created && await InvokeScriptAsync(
                () => scriptBridge.ActivateAsync(activePaneId),
                tab))
        {
            requestFocus(tab);
        }
    }

    public Task<bool> FlushOutputAsync() => outputCoordinator.FlushAsync();

    public void Acknowledge(HostedTerminal hosted, long token, bool success) =>
        outputCoordinator.Acknowledge(hosted, token, success);

    private void Synchronize(IEnumerable<TerminalTabViewModel>? tabs)
    {
        TerminalTabViewModel[] currentTabs = tabs?.ToArray() ?? [];
        var currentIds = currentTabs
            .SelectMany(tab => tab.Panes)
            .Select(pane => pane.PaneId)
            .ToHashSet();

        foreach (HostedTerminal hosted in terminals.Values)
        {
            if (!currentIds.Contains(hosted.Pane.PaneId))
            {
                RemoveHosted(hosted);
            }
        }

        foreach (TerminalTabViewModel tab in currentTabs)
        {
            Add(tab);
        }
    }

    private void OnCollectionChanged(
        object? sender,
        NotifyCollectionChangedEventArgs eventArgs)
    {
        if (eventArgs.OldItems is not null)
        {
            foreach (TerminalTabViewModel tab in eventArgs.OldItems)
            {
                Remove(tab);
            }
        }

        if (eventArgs.NewItems is not null)
        {
            foreach (TerminalTabViewModel tab in eventArgs.NewItems)
            {
                Add(tab);
            }
        }

        if (eventArgs.Action == NotifyCollectionChangedAction.Reset)
        {
            Synchronize(sender as IEnumerable<TerminalTabViewModel>);
        }
    }

    private void Add(TerminalTabViewModel tab)
    {
        tab.PaneOutputReceived -= OnPaneOutputReceived;
        tab.PaneOutputReceived += OnPaneOutputReceived;
        tab.PaneLayoutChanged -= OnPaneLayoutChanged;
        tab.PaneLayoutChanged += OnPaneLayoutChanged;
        tab.FocusRequested -= OnFocusRequested;
        tab.FocusRequested += OnFocusRequested;
        tab.AppearanceChanged -= OnAppearanceChanged;
        tab.AppearanceChanged += OnAppearanceChanged;
        tab.Terminating -= OnTabTerminating;
        tab.Terminating += OnTabTerminating;
        foreach (TerminalPaneViewModel pane in tab.Panes)
        {
            var hosted = new HostedTerminal(tab, pane);
            if (!terminals.TryAdd(pane.PaneId, hosted))
            {
                continue;
            }

            if (isHostReady())
            {
                _ = CreateAsync(hosted);
            }
        }
    }

    private void Remove(TerminalTabViewModel tab)
    {
        HostedTerminal[] hostedPanes = terminals.Values
            .Where(hosted => ReferenceEquals(hosted.Tab, tab))
            .ToArray();
        if (hostedPanes.Length == 0)
        {
            return;
        }

        tab.PaneOutputReceived -= OnPaneOutputReceived;
        tab.PaneLayoutChanged -= OnPaneLayoutChanged;
        tab.FocusRequested -= OnFocusRequested;
        tab.AppearanceChanged -= OnAppearanceChanged;
        tab.Terminating -= OnTabTerminating;
        foreach (HostedTerminal hosted in hostedPanes)
        {
            RemoveHosted(hosted);
        }
    }

    private void RemoveHosted(HostedTerminal hosted)
    {
        if (!terminals.TryRemove(hosted.Pane.PaneId, out _))
        {
            return;
        }

        hosted.Removed = true;
        outputCoordinator.Complete(hosted);
        if (isHostReady())
        {
            _ = DisposeAsync(hosted);
        }
    }

    private async Task CreateAsync(HostedTerminal hosted)
    {
        await hosted.CreationGate.WaitAsync();
        try
        {
            if (!isHostReady() || hosted.Created || hosted.Removed ||
                !terminals.ContainsKey(hosted.Pane.PaneId))
            {
                return;
            }

            await scriptBridge.CreateAsync(hosted.Tab, hosted.Pane);
            hosted.Created = true;
            await scriptBridge.LayoutAsync(hosted.Tab);
        }
        catch (Exception exception)
        {
            hosted.Created = false;
            hosted.Tab.ReportLaunchFailed(exception.Message);
        }
        finally
        {
            hosted.CreationGate.Release();
        }
    }

    private async Task DisposeAsync(HostedTerminal hosted)
    {
        await hosted.CreationGate.WaitAsync();
        try
        {
            if (!hosted.Created)
            {
                return;
            }

            try
            {
                await scriptBridge.DisposeAsync(hosted.Pane.PaneId);
            }
            catch
            {
                // The native host may already be shutting down.
            }

            hosted.Created = false;
        }
        finally
        {
            hosted.CreationGate.Release();
        }
    }

    private void OnPaneOutputReceived(object? sender, TerminalPaneOutputEventArgs eventArgs)
    {
        if (terminals.TryGetValue(eventArgs.PaneId, out HostedTerminal? hosted))
        {
            outputCoordinator.Enqueue(hosted, eventArgs.Output);
        }
    }

    private void OnPaneLayoutChanged(object? sender, EventArgs eventArgs)
    {
        if (sender is not TerminalTabViewModel tab)
        {
            return;
        }

        Synchronize(observedTabs);
        if (isHostReady())
        {
            _ = scriptBridge.LayoutAsync(tab);
        }
    }

    private void OnTabTerminating(object? sender, EventArgs eventArgs)
    {
        if (sender is TerminalTabViewModel tab &&
            terminals.TryGetValue(tab.Id, out HostedTerminal? hosted))
        {
            outputCoordinator.Complete(hosted);
        }
    }

    private void OnFocusRequested(object? sender, EventArgs eventArgs)
    {
        if (sender is TerminalTabViewModel tab && ReferenceEquals(tab, getActiveTab()))
        {
            requestFocus(tab);
        }
    }

    private void OnAppearanceChanged(object? sender, EventArgs eventArgs)
    {
        if (sender is TerminalTabViewModel tab &&
            isHostReady() &&
            terminals.Values.Any(hosted =>
                ReferenceEquals(hosted.Tab, tab) && hosted.Created))
        {
            _ = InvokeScriptAsync(() => scriptBridge.ConfigureAsync(tab), tab);
        }
    }

    private async Task<bool> InvokeScriptAsync(
        Func<Task> invokeScript,
        TerminalTabViewModel tab)
    {
        try
        {
            await invokeScript();
            return true;
        }
        catch (Exception exception)
        {
            tab.ReportLaunchFailed(exception.Message);
            return false;
        }
    }

    private HostedTerminal? GetActiveHostedTerminal() =>
        getActiveTab() is { ActivePaneId: Guid activePaneId } &&
        terminals.TryGetValue(activePaneId, out HostedTerminal? hosted)
            ? hosted
            : null;
}
