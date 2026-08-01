using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SuperTerminal.UI.ViewModels;

namespace SuperTerminal.UI.Controls;

public sealed class WebTerminalControl : NativeWebView
{
    public static readonly StyledProperty<TerminalTabViewModel?> TabProperty =
        AvaloniaProperty.Register<WebTerminalControl, TerminalTabViewModel?>(nameof(Tab));

    public static readonly StyledProperty<bool> IsActiveProperty =
        AvaloniaProperty.Register<WebTerminalControl, bool>(nameof(IsActive));

    private readonly ConcurrentQueue<string> pendingOutput = new();
    private int flushScheduled;
    private bool navigated;
    private bool terminalReady;

    public WebTerminalControl()
    {
        WebMessageReceived += OnWebMessageReceived;
    }

    public TerminalTabViewModel? Tab
    {
        get => GetValue(TabProperty);
        set => SetValue(TabProperty, value);
    }

    public bool IsActive
    {
        get => GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs eventArgs)
    {
        base.OnAttachedToVisualTree(eventArgs);
        if (Tab is not null)
        {
            Tab.TerminalOutputReceived += OnTerminalOutputReceived;
            Tab.FocusRequested += OnFocusRequested;
            Tab.AppearanceChanged += OnAppearanceChanged;
        }

        if (!navigated)
        {
            string pagePath = Path.Combine(
                AppContext.BaseDirectory,
                "WebTerminal",
                "dist",
                "index.html");
            Navigate(new Uri(pagePath));
            navigated = true;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs eventArgs)
    {
        if (Tab is not null)
        {
            Tab.TerminalOutputReceived -= OnTerminalOutputReceived;
            Tab.FocusRequested -= OnFocusRequested;
            Tab.AppearanceChanged -= OnAppearanceChanged;
        }

        base.OnDetachedFromVisualTree(eventArgs);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsActiveProperty && change.GetNewValue<bool>())
        {
            FocusTerminal();
        }
    }

    private async void OnWebMessageReceived(object? sender, WebMessageReceivedEventArgs eventArgs)
    {
        if (Tab is null || string.IsNullOrWhiteSpace(eventArgs.Body))
        {
            return;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(eventArgs.Body);
            JsonElement root = document.RootElement;
            string? type = root.GetProperty("type").GetString();
            switch (type)
            {
                case "ready":
                    terminalReady = true;
                    await ApplyAppearanceAsync();
                    await Tab.StartPtyAsync(
                        root.GetProperty("columns").GetInt32(),
                        root.GetProperty("rows").GetInt32());
                    ScheduleOutputFlush();
                    FocusTerminal();
                    break;
                case "input":
                    await Tab.WritePtyAsync(root.GetProperty("data").GetString() ?? string.Empty);
                    break;
                case "resize":
                    Tab.ResizePty(
                        root.GetProperty("columns").GetInt32(),
                        root.GetProperty("rows").GetInt32());
                    break;
                case "applicationCommand":
                    Tab.RequestApplicationCommand(
                        root.GetProperty("command").GetString() ?? string.Empty);
                    break;
            }
        }
        catch (Exception exception) when (
            exception is JsonException or InvalidOperationException)
        {
            Tab.ReportLaunchFailed(exception.Message);
        }
    }

    private void OnTerminalOutputReceived(object? sender, string output)
    {
        pendingOutput.Enqueue(output);
        ScheduleOutputFlush();
    }

    private void OnFocusRequested(object? sender, EventArgs eventArgs) =>
        FocusTerminal();

    private void OnAppearanceChanged(object? sender, EventArgs eventArgs)
    {
        if (terminalReady)
        {
            Dispatcher.UIThread.Post(
                async () => await ApplyAppearanceAsync(),
                DispatcherPriority.Render);
        }
    }

    private async Task ApplyAppearanceAsync()
    {
        if (!terminalReady || Tab is null)
        {
            return;
        }

        string options = JsonSerializer.Serialize(new
        {
            fontFamily = Tab.FontFamily,
            fontSize = Tab.FontSize,
            cursorStyle = Tab.CursorStyle.ToLowerInvariant(),
            cursorBlink = Tab.CursorBlink,
        });
        await InvokeScript($"window.terminalConfigure({options})");
    }

    private void ScheduleOutputFlush()
    {
        if (!terminalReady || Interlocked.Exchange(ref flushScheduled, 1) != 0)
        {
            return;
        }

        Dispatcher.UIThread.Post(FlushOutputAsync, DispatcherPriority.Render);
    }

    private async void FlushOutputAsync()
    {
        try
        {
            var output = new StringBuilder();
            while (pendingOutput.TryDequeue(out string? chunk))
            {
                output.Append(chunk);
            }

            if (output.Length > 0)
            {
                string base64 = Convert.ToBase64String(
                    Encoding.UTF8.GetBytes(output.ToString()));
                await InvokeScript($"window.terminalWriteBase64('{base64}')");
            }
        }
        finally
        {
            Interlocked.Exchange(ref flushScheduled, 0);
            if (!pendingOutput.IsEmpty)
            {
                ScheduleOutputFlush();
            }
        }
    }

    private void FocusTerminal()
    {
        if (!terminalReady || !IsActive)
        {
            return;
        }

        Dispatcher.UIThread.Post(
            async () =>
            {
                Focus();
                await InvokeScript("window.terminalFocus()");
            },
            DispatcherPriority.Input);
    }
}
