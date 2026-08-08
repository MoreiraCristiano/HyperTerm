using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HyperTerm.UI.Services;
using HyperTerm.UI.ViewModels;

namespace HyperTerm.UI.Controls;

public sealed class WebTerminalHostControl : NativeWebView
{
    public static readonly StyledProperty<IEnumerable<TerminalTabViewModel>?> TabsProperty =
        AvaloniaProperty.Register<WebTerminalHostControl, IEnumerable<TerminalTabViewModel>?>(
            nameof(Tabs));

    public static readonly StyledProperty<TerminalTabViewModel?> ActiveTabProperty =
        AvaloniaProperty.Register<WebTerminalHostControl, TerminalTabViewModel?>(
            nameof(ActiveTab));

    private readonly ConcurrentDictionary<Guid, HostedTerminal> terminals = new();
    private INotifyCollectionChanged? observedCollection;
    private bool hostReady;
    private bool navigated;
    private int flushScheduled;
    private bool focusAfterActivationPending;

    public WebTerminalHostControl()
    {
        WebMessageReceived += OnWebMessageReceived;
    }

    public IEnumerable<TerminalTabViewModel>? Tabs
    {
        get => GetValue(TabsProperty);
        set => SetValue(TabsProperty, value);
    }

    public TerminalTabViewModel? ActiveTab
    {
        get => GetValue(ActiveTabProperty);
        set => SetValue(ActiveTabProperty, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ObserveTabs(Tabs);

        if (!navigated)
        {
            string pagePath = ResolveWebTerminalPagePath();
            Navigate(new Uri(pagePath));
            navigated = true;
        }
    }

    private static string ResolveWebTerminalPagePath()
    {
        string relativePath = Path.Combine("WebTerminal", "dist", "index.html");
        string deployedPath = Path.Combine(AppContext.BaseDirectory, relativePath);
        if (File.Exists(deployedPath))
        {
            return deployedPath;
        }

        using Process process = Process.GetCurrentProcess();
        foreach (ProcessModule module in process.Modules)
        {
            string? moduleDirectory = Path.GetDirectoryName(module.FileName);
            if (string.IsNullOrEmpty(moduleDirectory))
            {
                continue;
            }

            string extractedPath = Path.Combine(moduleDirectory, relativePath);
            if (File.Exists(extractedPath))
            {
                return extractedPath;
            }
        }

        throw new FileNotFoundException(
            "The web terminal page was not found in the application or bundle extraction directories.",
            deployedPath);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        StopObservingTabs();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == TabsProperty)
        {
            ObserveTabs(change.GetNewValue<IEnumerable<TerminalTabViewModel>?>());
        }
        else if (change.Property == ActiveTabProperty && hostReady)
        {
            _ = ActivateTerminalAsync(change.GetNewValue<TerminalTabViewModel?>());
        }
        else if (change.Property == IsVisibleProperty &&
                 change.GetNewValue<bool>() && hostReady)
        {
            _ = ActivateTerminalAsync(ActiveTab);
        }
    }

    private async void OnWebMessageReceived(
        object? sender,
        WebMessageReceivedEventArgs eventArgs)
    {
        if (!WebTerminalMessage.TryParse(eventArgs.Body, out WebTerminalMessage? parsed) ||
            parsed is not { } message)
        {
            return;
        }

        try
        {
            if (message.Type == "hostReady")
            {
                if (hostReady)
                {
                    return;
                }

                hostReady = true;
                await CreateExistingTerminalsAsync();
                await ActivateTerminalAsync(ActiveTab);
                ScheduleOutputFlush();
                await FocusAfterWindowActivationIfReadyAsync();
                return;
            }

            if (message.TabId is not Guid tabId ||
                !terminals.TryGetValue(tabId, out HostedTerminal? hosted))
            {
                return;
            }

            switch (message.Type)
            {
                case "ready":
                    await hosted.Tab.StartPtyAsync(
                        message.Columns,
                        message.Rows);
                    ScheduleOutputFlush();
                    if (ReferenceEquals(hosted.Tab, ActiveTab))
                    {
                        await FocusTerminalAsync(hosted.Tab);
                    }
                    break;
                case "input":
                    await hosted.Tab.WritePtyAsync(message.Data!);
                    break;
                case "copy":
                    await CopySelectionAsync(hosted.Tab, message.Data!);
                    break;
                case "paste":
                    await PasteClipboardAsync(hosted.Tab);
                    break;
                case "resize":
                    hosted.Tab.ResizePty(message.Columns, message.Rows);
                    break;
                case "applicationCommand":
                    hosted.Tab.RequestApplicationCommand(message.Command!);
                    break;
                case "writeComplete":
                    CompleteOutputWrite(hosted, message.Token, message.Success);
                    break;
            }
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or COMException)
        {
            ActiveTab?.ReportLaunchFailed(exception.Message);
        }
    }

    private void ObserveTabs(IEnumerable<TerminalTabViewModel>? tabs)
    {
        if (ReferenceEquals(observedCollection, tabs))
        {
            SynchronizeTabs(tabs);
            return;
        }

        StopObservingTabs();
        observedCollection = tabs as INotifyCollectionChanged;
        if (observedCollection is not null)
        {
            observedCollection.CollectionChanged += OnTabsCollectionChanged;
        }

        SynchronizeTabs(tabs);
    }

    private void StopObservingTabs()
    {
        if (observedCollection is not null)
        {
            observedCollection.CollectionChanged -= OnTabsCollectionChanged;
            observedCollection = null;
        }

        foreach (HostedTerminal hosted in terminals.Values.ToArray())
        {
            RemoveTerminal(hosted.Tab);
        }
    }

    private void SynchronizeTabs(IEnumerable<TerminalTabViewModel>? tabs)
    {
        TerminalTabViewModel[] currentTabs = tabs?.ToArray() ?? [];
        var currentIds = currentTabs.Select(tab => tab.Id).ToHashSet();

        foreach (HostedTerminal hosted in terminals.Values)
        {
            if (!currentIds.Contains(hosted.Tab.Id))
            {
                RemoveTerminal(hosted.Tab);
            }
        }

        foreach (TerminalTabViewModel tab in currentTabs)
        {
            AddTerminal(tab);
        }
    }

    private void OnTabsCollectionChanged(
        object? sender,
        NotifyCollectionChangedEventArgs eventArgs)
    {
        if (eventArgs.OldItems is not null)
        {
            foreach (TerminalTabViewModel tab in eventArgs.OldItems)
            {
                RemoveTerminal(tab);
            }
        }

        if (eventArgs.NewItems is not null)
        {
            foreach (TerminalTabViewModel tab in eventArgs.NewItems)
            {
                AddTerminal(tab);
            }
        }

        if (eventArgs.Action == NotifyCollectionChangedAction.Reset)
        {
            SynchronizeTabs(Tabs);
        }
    }

    private void AddTerminal(TerminalTabViewModel tab)
    {
        var hosted = new HostedTerminal(tab);
        if (!terminals.TryAdd(tab.Id, hosted))
        {
            return;
        }

        tab.TerminalOutputReceived += OnTerminalOutputReceived;
        tab.FocusRequested += OnFocusRequested;
        tab.AppearanceChanged += OnAppearanceChanged;
        tab.Terminating += OnTabTerminating;

        if (hostReady)
        {
            _ = CreateTerminalAsync(hosted);
        }
    }

    private void RemoveTerminal(TerminalTabViewModel tab)
    {
        if (!terminals.TryRemove(tab.Id, out HostedTerminal? hosted))
        {
            return;
        }

        tab.TerminalOutputReceived -= OnTerminalOutputReceived;
        tab.FocusRequested -= OnFocusRequested;
        tab.AppearanceChanged -= OnAppearanceChanged;
        tab.Terminating -= OnTabTerminating;
        hosted.Removed = true;
        hosted.Output.Complete();
        hosted.WriteTimeoutCancellation?.Cancel();
        hosted.WriteTimeoutCancellation?.Dispose();
        hosted.WriteTimeoutCancellation = null;

        if (hostReady)
        {
            _ = DisposeTerminalAsync(hosted);
        }
    }

    private async Task CreateExistingTerminalsAsync()
    {
        HostedTerminal[] existing = terminals.Values
            .OrderByDescending(hosted => ReferenceEquals(hosted.Tab, ActiveTab))
            .ToArray();
        foreach (HostedTerminal hosted in existing)
        {
            await CreateTerminalAsync(hosted);
        }
    }

    private async Task CreateTerminalAsync(HostedTerminal hosted)
    {
        await hosted.CreationGate.WaitAsync();
        try
        {
            if (!hostReady || hosted.Created || hosted.Removed ||
                !terminals.ContainsKey(hosted.Tab.Id))
            {
                return;
            }

            string request = JsonSerializer.Serialize(new
            {
                tabId = GetTerminalId(hosted.Tab),
                options = CreateAppearanceOptions(hosted.Tab),
            });
            await InvokeScript($"window.terminalHost.create({request})");
            hosted.Created = true;
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

    private async Task DisposeTerminalAsync(HostedTerminal hosted)
    {
        await hosted.CreationGate.WaitAsync();
        try
        {
            if (hosted.Created)
            {
                try
                {
                    await InvokeScript(
                        $"window.terminalHost.dispose('{GetTerminalId(hosted.Tab)}')");
                }
                catch
                {
                    // The native host may already be shutting down.
                }
                hosted.Created = false;
            }
        }
        finally
        {
            hosted.CreationGate.Release();
        }
    }

    private async Task ActivateTerminalAsync(TerminalTabViewModel? tab)
    {
        if (!hostReady || tab is null ||
            !terminals.TryGetValue(tab.Id, out HostedTerminal? hosted))
        {
            return;
        }

        if (!hosted.Created)
        {
            await CreateTerminalAsync(hosted);
        }

        if (hosted.Created)
        {
            await InvokeTerminalScriptAsync(
                $"window.terminalHost.activate('{GetTerminalId(tab)}')",
                tab);
            RequestTerminalFocus(tab);
        }
    }

    private void OnTerminalOutputReceived(object? sender, string output)
    {
        if (sender is not TerminalTabViewModel tab ||
            !terminals.TryGetValue(tab.Id, out HostedTerminal? hosted) ||
            !hosted.Output.Enqueue(output))
        {
            return;
        }

        ScheduleOutputFlush();
    }

    private void OnTabTerminating(object? sender, EventArgs eventArgs)
    {
        if (sender is TerminalTabViewModel tab &&
            terminals.TryGetValue(tab.Id, out HostedTerminal? hosted))
        {
            hosted.Output.Complete();
        }
    }

    private void ScheduleOutputFlush()
    {
        if (!hostReady || Interlocked.Exchange(ref flushScheduled, 1) != 0)
        {
            return;
        }

        Dispatcher.UIThread.Post(FlushOutputAsync, DispatcherPriority.Render);
    }

    private async void FlushOutputAsync()
    {
        Interlocked.Exchange(ref flushScheduled, 0);
        if (!hostReady)
        {
            return;
        }

        int writesStarted = 0;
        HostedTerminal? activeHosted = ActiveTab is { } activeTab &&
                                       terminals.TryGetValue(activeTab.Id, out HostedTerminal? match)
            ? match
            : null;
        if (activeHosted is not null && await TryStartOutputWriteAsync(activeHosted))
        {
            writesStarted++;
        }

        foreach (HostedTerminal hosted in terminals.Values)
        {
            if (writesStarted >= 4)
            {
                break;
            }

            if (ReferenceEquals(hosted, activeHosted) ||
                !await TryStartOutputWriteAsync(hosted))
            {
                continue;
            }

            writesStarted++;
        }

        if (terminals.Values.Any(hosted =>
                hosted.Created && !hosted.WriteInFlight && hosted.Output.HasData))
        {
            ScheduleOutputFlush();
        }
    }

    private void CompleteOutputWrite(HostedTerminal hosted, long token, bool success)
    {
        if (hosted.WriteInFlight && hosted.WriteToken == token)
        {
            hosted.WriteTimeoutCancellation?.Cancel();
            hosted.WriteTimeoutCancellation?.Dispose();
            hosted.WriteTimeoutCancellation = null;
            hosted.WriteInFlight = false;
            if (!success)
            {
                hosted.Tab.ReportLaunchFailed("Terminal renderer rejected output.");
            }
            ScheduleOutputFlush();
        }
    }

    private void OnFocusRequested(object? sender, EventArgs eventArgs)
    {
        if (sender is TerminalTabViewModel tab && ReferenceEquals(tab, ActiveTab))
        {
            RequestTerminalFocus(tab);
        }
    }

    private void RequestTerminalFocus(TerminalTabViewModel tab)
    {
        Dispatcher.UIThread.Post(
            async () => await FocusTerminalAsync(tab),
            DispatcherPriority.Input);
    }

    private async Task FocusTerminalAsync(TerminalTabViewModel tab)
    {
        if (!hostReady || !IsVisible || !ReferenceEquals(tab, ActiveTab))
        {
            return;
        }

        Focus();
        await InvokeTerminalScriptAsync(
            $"window.terminalHost.focus('{GetTerminalId(tab)}')",
            tab);
    }

    public void FocusAfterWindowActivation()
    {
        focusAfterActivationPending = true;
        _ = FocusAfterWindowActivationIfReadyAsync();
    }

    public void CancelWindowActivationFocus() =>
        focusAfterActivationPending = false;

    private async Task FocusAfterWindowActivationIfReadyAsync()
    {
        TerminalTabViewModel? tab = ActiveTab;
        if (!focusAfterActivationPending || !hostReady || !IsVisible || tab is null)
        {
            return;
        }

        focusAfterActivationPending = false;
        try
        {
            Focus();
            WindowsWebViewFocus.TryMoveFocus(this);
            await InvokeTerminalScriptAsync(
                $"window.terminalHost.focus('{GetTerminalId(tab)}')",
                tab);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or COMException)
        {
            tab.ReportLaunchFailed(exception.Message);
        }
    }

    private async Task<bool> TryStartOutputWriteAsync(HostedTerminal hosted)
    {
        if (!hosted.Created || hosted.WriteInFlight)
        {
            return false;
        }

        string? output = hosted.Output.TryDrainBatch();
        if (output is null)
        {
            return false;
        }

        long token = ++hosted.WriteToken;
        hosted.WriteInFlight = true;
        try
        {
            string jsonOutput = JsonSerializer.Serialize(output);
            await InvokeScript(
                $"window.terminalHost.write('{GetTerminalId(hosted.Tab)}', {token}, {jsonOutput})");
            hosted.WriteTimeoutCancellation?.Dispose();
            hosted.WriteTimeoutCancellation = new CancellationTokenSource();
            _ = ObserveWriteTimeoutAsync(
                hosted,
                token,
                hosted.WriteTimeoutCancellation.Token);
            return true;
        }
        catch (Exception exception)
        {
            hosted.WriteInFlight = false;
            hosted.Tab.ReportLaunchFailed(exception.Message);
            return false;
        }
    }

    private async Task ObserveWriteTimeoutAsync(
        HostedTerminal hosted,
        long token,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        if (!hosted.Removed && hosted.WriteInFlight && hosted.WriteToken == token)
        {
            hosted.WriteInFlight = false;
            hosted.Tab.ReportLaunchFailed("Terminal renderer stopped acknowledging output.");
            ScheduleOutputFlush();
        }
    }

    private async void OnAppearanceChanged(object? sender, EventArgs eventArgs)
    {
        if (sender is not TerminalTabViewModel tab ||
            !terminals.TryGetValue(tab.Id, out HostedTerminal? hosted) ||
            !hostReady || !hosted.Created)
        {
            return;
        }

        string request = JsonSerializer.Serialize(new
        {
            tabId = GetTerminalId(tab),
            options = CreateAppearanceOptions(tab),
        });
        await InvokeTerminalScriptAsync(
            $"window.terminalHost.configure({request})",
            tab);
    }

    private async Task CopySelectionAsync(TerminalTabViewModel tab, string text)
    {
        if (text.Length == 0)
        {
            return;
        }

        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is null)
            {
                tab.ReportCopyFailed("Windows clipboard is unavailable");
                return;
            }

            await clipboard.SetTextAsync(text);
            tab.ReportTextCopied(text.Length);
        }
        catch (Exception exception)
        {
            tab.ReportCopyFailed(exception.Message);
        }
    }

    private async Task PasteClipboardAsync(TerminalTabViewModel tab)
    {
        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is null)
            {
                tab.ReportPasteFailed("Windows clipboard is unavailable");
                return;
            }

            string? text = await clipboard.TryGetTextAsync();
            if (!string.IsNullOrEmpty(text))
            {
                await tab.WritePtyAsync(text);
            }
        }
        catch (Exception exception)
        {
            tab.ReportPasteFailed(exception.Message);
        }
    }

    private static object CreateAppearanceOptions(TerminalTabViewModel tab) => new
    {
        fontFamily = tab.FontFamily,
        fontSize = tab.FontSize,
        selectionBackground = tab.SelectionColor,
        cursorStyle = tab.CursorStyle.ToLowerInvariant(),
        cursorBlink = tab.CursorBlink,
    };

    private async Task<bool> InvokeTerminalScriptAsync(
        string script,
        TerminalTabViewModel tab)
    {
        try
        {
            await InvokeScript(script);
            return true;
        }
        catch (Exception exception)
        {
            tab.ReportLaunchFailed(exception.Message);
            return false;
        }
    }

    private static string GetTerminalId(TerminalTabViewModel tab) =>
        tab.Id.ToString("N");

    private sealed class HostedTerminal(TerminalTabViewModel tab)
    {
        public TerminalTabViewModel Tab { get; } = tab;

        public TerminalOutputBuffer Output { get; } = new();

        public SemaphoreSlim CreationGate { get; } = new(1, 1);

        public bool Created { get; set; }

        public bool Removed { get; set; }

        public bool WriteInFlight { get; set; }

        public long WriteToken { get; set; }

        public CancellationTokenSource? WriteTimeoutCancellation { get; set; }
    }
}
