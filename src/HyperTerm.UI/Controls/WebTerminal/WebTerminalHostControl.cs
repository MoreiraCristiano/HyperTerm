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

    private readonly WebTerminalScriptBridge scriptBridge;
    private readonly HostedTerminalRegistry terminalRegistry;
    private readonly WebTerminalClipboard clipboard;
    private bool hostReady;
    private bool navigated;
    private int flushScheduled;
    private bool focusAfterActivationPending;

    public WebTerminalHostControl()
    {
        scriptBridge = new WebTerminalScriptBridge(
            async script => await InvokeScript(script));
        terminalRegistry = new HostedTerminalRegistry(
            scriptBridge,
            () => ActiveTab,
            () => hostReady,
            RequestTerminalFocus,
            ScheduleOutputFlush);
        clipboard = new WebTerminalClipboard(
            () => TopLevel.GetTopLevel(this)?.Clipboard);
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
        terminalRegistry.Observe(Tabs);

        if (!navigated)
        {
            string pagePath = WebTerminalPageResolver.Resolve();
            Navigate(new Uri(pagePath));
            navigated = true;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        terminalRegistry.StopObserving();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == TabsProperty)
        {
            terminalRegistry.Observe(change.GetNewValue<IEnumerable<TerminalTabViewModel>?>());
        }
        else if (change.Property == ActiveTabProperty && hostReady)
        {
            _ = terminalRegistry.ActivateAsync(
                change.GetNewValue<TerminalTabViewModel?>());
        }
        else if (change.Property == IsVisibleProperty &&
                 change.GetNewValue<bool>() && hostReady)
        {
            _ = terminalRegistry.ActivateAsync(ActiveTab);
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
                await terminalRegistry.CreateExistingAsync();
                await terminalRegistry.ActivateAsync(ActiveTab);
                ScheduleOutputFlush();
                await FocusAfterWindowActivationIfReadyAsync();
                return;
            }

            if (message.TabId is not Guid tabId ||
                !terminalRegistry.TryGet(tabId, out HostedTerminal hosted))
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
                    await clipboard.CopyAsync(hosted.Tab, message.Data!);
                    break;
                case "paste":
                    await clipboard.PasteAsync(hosted.Tab);
                    break;
                case "resize":
                    hosted.Tab.ResizePty(message.Columns, message.Rows);
                    break;
                case "applicationCommand":
                    hosted.Tab.RequestApplicationCommand(message.Command!);
                    break;
                case "writeComplete":
                    terminalRegistry.Acknowledge(hosted, message.Token, message.Success);
                    break;
            }
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or COMException)
        {
            ActiveTab?.ReportLaunchFailed(exception.Message);
        }
    }

    private void ScheduleOutputFlush()
    {
        if (!hostReady || Interlocked.Exchange(ref flushScheduled, 1) != 0)
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
            ActiveTab?.ReportLaunchFailed(exception.Message);
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
            () => scriptBridge.FocusAsync(tab.Id),
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
        TerminalTabViewModel? tab = ActiveTab;
        if (!hostReady || !IsVisible || tab is null)
        {
            return;
        }

        await InvokeTerminalScriptAsync(
            () => scriptBridge.OpenSearchAsync(tab.Id),
            tab);
    }

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
                () => scriptBridge.FocusAsync(tab.Id),
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
