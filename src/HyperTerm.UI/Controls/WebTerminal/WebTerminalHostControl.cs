using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
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

    public static readonly StyledProperty<TerminalTabViewModel?> TabProperty =
        AvaloniaProperty.Register<WebTerminalHostControl, TerminalTabViewModel?>(nameof(Tab));

    private readonly WebTerminalScriptBridge scriptBridge;
    private readonly HostedTerminalRegistry terminalRegistry;
    private readonly WebTerminalClipboard clipboard;
    private bool hostReady;
    private bool navigated;
    private int flushScheduled;
    private bool focusAfterActivationPending;
    private bool preparedForRemoval;

    public WebTerminalHostControl()
    {
        scriptBridge = new WebTerminalScriptBridge(
            async script => await InvokeScript(script));
        terminalRegistry = new HostedTerminalRegistry(
            scriptBridge,
            () => CurrentTab,
            () => hostReady,
            RequestTerminalFocus,
            ScheduleOutputFlush);
        clipboard = new WebTerminalClipboard(
            () => TopLevel.GetTopLevel(this)?.Clipboard);
        AdapterCreated += OnAdapterCreated;
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

    public TerminalTabViewModel? Tab
    {
        get => GetValue(TabProperty);
        set => SetValue(TabProperty, value);
    }

    private TerminalTabViewModel? CurrentTab => Tab ?? ActiveTab;

    private IEnumerable<TerminalTabViewModel>? ObservedTabs =>
        Tab is null ? Tabs : [Tab];

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (preparedForRemoval)
        {
            return;
        }

        terminalRegistry.Observe(ObservedTabs);

        if (!navigated)
        {
            string pagePath = WebTerminalPageResolver.Resolve();
            Navigate(new Uri(pagePath));
            navigated = true;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        PrepareForRemoval();
        base.OnDetachedFromVisualTree(e);
    }

    internal void PrepareForRemoval()
    {
        if (preparedForRemoval)
        {
            return;
        }

        preparedForRemoval = true;
        focusAfterActivationPending = false;
        Interlocked.Exchange(ref flushScheduled, 0);
        terminalRegistry.StopObserving();
        hostReady = false;
        AdapterCreated -= OnAdapterCreated;
        WebMessageReceived -= OnWebMessageReceived;
        (TryGetPlatformHandle() as IDisposable)?.Dispose();
        Tabs = null;
        ActiveTab = null;
        Tab = null;
    }

    private void OnAdapterCreated(object? sender, WebViewAdapterEventArgs eventArgs) =>
        WindowsWebViewSettings.TryDisableBrowserAccelerators(this);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        if (preparedForRemoval)
        {
            return;
        }

        base.OnPropertyChanged(change);

        if (change.Property == TabsProperty)
        {
            terminalRegistry.Observe(ObservedTabs);
        }
        else if (change.Property == TabProperty)
        {
            terminalRegistry.Observe(ObservedTabs);
            if (hostReady)
            {
                _ = terminalRegistry.ActivateAsync(CurrentTab);
            }
        }
        else if (change.Property == ActiveTabProperty && hostReady && Tab is null)
        {
            _ = terminalRegistry.ActivateAsync(
                change.GetNewValue<TerminalTabViewModel?>());
        }
        else if (change.Property == IsVisibleProperty &&
                 change.GetNewValue<bool>() && hostReady)
        {
            _ = terminalRegistry.ActivateAsync(CurrentTab);
        }
    }

    private async void OnWebMessageReceived(
        object? sender,
        WebMessageReceivedEventArgs eventArgs)
    {
        if (preparedForRemoval)
        {
            return;
        }

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
                await terminalRegistry.CreateExistingAsync();
                ScheduleOutputFlush();
                await FocusAfterWindowActivationIfReadyAsync();
                return;
            }

            if (message.TabId is not Guid tabId)
            {
                return;
            }

            Guid paneId = message.PaneId ?? tabId;
            if (!terminalRegistry.TryGet(paneId, out HostedTerminal hosted))
            {
                return;
            }

            switch (message.Type)
            {
                case "ready":
                    await hosted.Tab.StartPaneAsync(
                        paneId,
                        message.Columns,
                        message.Rows);
                    ScheduleOutputFlush();
                    if (ReferenceEquals(hosted.Tab, CurrentTab))
                    {
                        await FocusTerminalAsync(hosted.Tab);
                    }
                    break;
                case "input":
                    await hosted.Tab.WritePaneAsync(paneId, message.Data!);
                    break;
                case "copy":
                    await clipboard.CopyAsync(hosted.Tab, message.Data!);
                    break;
                case "paste":
                    await clipboard.PasteAsync(hosted.Tab);
                    break;
                case "resize":
                    hosted.Tab.ResizePane(paneId, message.Columns, message.Rows);
                    break;
                case "applicationCommand":
                    hosted.Tab.SetActivePane(paneId);
                    hosted.Tab.RequestApplicationCommand(message.Command!);
                    break;
                case "paneActivated":
                    hosted.Tab.SetActivePane(paneId);
                    break;
                case "paneRatio":
                    hosted.Tab.SetPaneRatio(paneId, message.Ratio);
                    break;
                case "writeComplete":
                    terminalRegistry.Acknowledge(hosted, message.Token, message.Success);
                    break;
            }
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or COMException)
        {
            CurrentTab?.ReportLaunchFailed(exception.Message);
        }
    }

    private void ScheduleOutputFlush()
    {
        if (preparedForRemoval ||
            !hostReady ||
            Interlocked.Exchange(ref flushScheduled, 1) != 0)
        {
            return;
        }

        Dispatcher.UIThread.Post(
            () => _ = FlushOutputAsync(),
            DispatcherPriority.Render);
    }

    private async Task FlushOutputAsync()
    {
        Interlocked.Exchange(ref flushScheduled, 0);
        if (!hostReady)
        {
            return;
        }

        try
        {
            if (await terminalRegistry.FlushOutputAsync())
            {
                ScheduleOutputFlush();
            }
        }
        catch (Exception exception)
        {
            CurrentTab?.ReportLaunchFailed(exception.Message);
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
        if (!hostReady || !IsVisible || !ReferenceEquals(tab, CurrentTab))
        {
            return;
        }

        Focus();
        WindowsWebViewFocus.TryMoveFocus(this);
        await InvokeTerminalScriptAsync(
            () => scriptBridge.FocusAsync(tab.ActivePaneId ?? tab.Id),
            tab);
    }

    public void FocusAfterWindowActivation()
    {
        focusAfterActivationPending = true;
        _ = FocusAfterWindowActivationIfReadyAsync();
    }

    public void CancelWindowActivationFocus() =>
        focusAfterActivationPending = false;

    public async Task OpenSearchAsync()
    {
        TerminalTabViewModel? tab = CurrentTab;
        if (!hostReady || !IsVisible || tab is null)
        {
            return;
        }

        await InvokeTerminalScriptAsync(
            () => scriptBridge.OpenSearchAsync(tab.ActivePaneId ?? tab.Id),
            tab);
    }

    private async Task FocusAfterWindowActivationIfReadyAsync()
    {
        TerminalTabViewModel? tab = CurrentTab;
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
                () => scriptBridge.FocusAsync(tab.ActivePaneId ?? tab.Id),
                tab);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or COMException)
        {
            tab.ReportLaunchFailed(exception.Message);
        }
    }

    private async Task<bool> InvokeTerminalScriptAsync(
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

}
