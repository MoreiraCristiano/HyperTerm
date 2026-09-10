using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using HyperTerm.UI.Controls;
using HyperTerm.UI.Views;
using Microsoft.Extensions.Logging.Abstractions;

namespace HyperTerm.UI.Tests;

public sealed class TerminalOverlayPresentationTests
{
    [AvaloniaFact]
    public void Preview_scales_with_terminal_viewport_after_window_grows()
    {
        var window = new MainWindow();
        Image preview = window.FindControl<Image>("TerminalPreview")!;
        ((Panel)preview.Parent!).Children.Remove(preview);
        Grid.SetRow(preview, 0);
        Grid.SetColumn(preview, 0);
        var viewport = new Grid { Children = { preview } };
        using var bitmap = new WriteableBitmap(
            new PixelSize(800, 600), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        preview.Source = bitmap;
        viewport.Measure(new Size(1200, 760));
        viewport.Arrange(new Rect(0, 0, 1200, 760));

        viewport.Measure(new Size(1920, 1080));
        viewport.Arrange(new Rect(0, 0, 1920, 1080));

        Assert.Equal(new Size(1440, 1080), preview.Bounds.Size);
        preview.Source = null;
    }

    [AvaloniaFact]
    public async Task Captures_visible_terminal_then_preserves_image_until_overlay_closes()
    {
        bool visible = false;
        Bitmap? displayed = null;
        var completion = new TaskCompletionSource<Bitmap?>();
        int captures = 0;
        using var presentation = new TerminalOverlayPresentation(
            _ =>
            {
                Assert.True(visible);
                captures++;
                return completion.Task;
            },
            (value, image) => { visible = value; displayed = image; },
            NullLogger.Instance);
        Guid tab = Guid.NewGuid();
        await presentation.UpdateAsync(tab, false);

        Task opening = presentation.UpdateAsync(tab, true);
        Assert.True(visible);
        Assert.Null(displayed);
        Bitmap preview = CreatePreview();
        completion.SetResult(preview);
        await opening;
        Assert.False(visible);
        Assert.Same(preview, displayed);

        await presentation.UpdateAsync(tab, true);
        Assert.Equal(1, captures);
        Assert.Same(preview, displayed);
        await presentation.UpdateAsync(tab, false);
        Assert.True(visible);
        Assert.Null(displayed);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Close_or_dispose_invalidates_pending_capture(bool dispose)
    {
        var completion = new TaskCompletionSource<Bitmap?>();
        bool visible = false;
        Bitmap? displayed = null;
        CancellationToken captureToken = default;
        using var presentation = new TerminalOverlayPresentation(
            token => { captureToken = token; return completion.Task; },
            (value, image) => { visible = value; displayed = image; },
            NullLogger.Instance);
        Guid tab = Guid.NewGuid();
        await presentation.UpdateAsync(tab, false);
        Task opening = presentation.UpdateAsync(tab, true);
        if (dispose)
        {
            presentation.Dispose();
            presentation.Dispose();
        }
        else
        {
            await presentation.UpdateAsync(tab, false);
        }

        Assert.True(captureToken.IsCancellationRequested);
        completion.SetResult(CreatePreview());
        await opening;
        Assert.Null(displayed);
        Assert.Equal(!dispose, visible);
    }

    [AvaloniaFact]
    public async Task Older_capture_cannot_replace_new_overlay_image()
    {
        var older = new TaskCompletionSource<Bitmap?>();
        var newer = new TaskCompletionSource<Bitmap?>();
        int captures = 0;
        Bitmap? displayed = null;
        using var presentation = new TerminalOverlayPresentation(
            _ => ++captures == 1 ? older.Task : newer.Task,
            (_, image) => displayed = image,
            NullLogger.Instance);
        Guid tab = Guid.NewGuid();
        await presentation.UpdateAsync(tab, false);
        Task first = presentation.UpdateAsync(tab, true);
        await presentation.UpdateAsync(tab, false);
        Task second = presentation.UpdateAsync(tab, true);
        Bitmap expected = CreatePreview();
        newer.SetResult(expected);
        await second;
        older.SetResult(CreatePreview());
        await first;
        Assert.Same(expected, displayed);
    }

    [AvaloniaFact]
    public async Task Changing_tab_behind_overlay_discards_previous_image()
    {
        Bitmap? displayed = null;
        int captures = 0;
        using var presentation = new TerminalOverlayPresentation(
            _ => { captures++; return Task.FromResult<Bitmap?>(CreatePreview()); },
            (_, image) => displayed = image,
            NullLogger.Instance);
        Guid tab = Guid.NewGuid();
        await presentation.UpdateAsync(tab, false);
        await presentation.UpdateAsync(tab, true);
        Assert.NotNull(displayed);
        await presentation.UpdateAsync(Guid.NewGuid(), true);
        Assert.Null(displayed);
        Assert.Equal(1, captures);
        await presentation.UpdateAsync(null, true);
        Assert.Null(displayed);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unavailable_or_failed_capture_still_hides_host_and_allows_restoration(bool fails)
    {
        bool visible = false;
        using var presentation = new TerminalOverlayPresentation(
            _ => fails
                ? Task.FromException<Bitmap?>(new InvalidOperationException("Capture unavailable"))
                : Task.FromResult<Bitmap?>(null),
            (value, image) => { visible = value; Assert.Null(image); },
            NullLogger.Instance);
        Guid tab = Guid.NewGuid();
        await presentation.UpdateAsync(tab, false);
        await presentation.UpdateAsync(tab, true);
        Assert.False(visible);
        await presentation.UpdateAsync(tab, false);
        Assert.True(visible);
    }

    [Theory]
    [InlineData("input", true)]
    [InlineData("paste", true)]
    [InlineData("copy", true)]
    [InlineData("applicationCommand", true)]
    [InlineData("paneActivated", true)]
    [InlineData("paneRatio", true)]
    [InlineData("writeComplete", false)]
    [InlineData("ready", false)]
    [InlineData("resize", false)]
    public void Blocking_interaction_preserves_output_and_lifecycle_messages(string type, bool blocked)
    {
        Assert.Equal(blocked, WebTerminalHostControl.IsInteractiveMessage(type));
    }

    [AvaloniaFact]
    public async Task Timeout_hides_native_host_without_waiting_for_browser_completion()
    {
        var time = new ManualTimeProvider();
        bool visible = false;
        using var presentation = new TerminalOverlayPresentation(
            async token =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return null;
            },
            (value, image) => { visible = value; Assert.Null(image); },
            NullLogger.Instance,
            time);
        Guid tab = Guid.NewGuid();
        await presentation.UpdateAsync(tab, false);
        Task opening = presentation.UpdateAsync(tab, true);
        Assert.False(opening.IsCompleted);
        Assert.Equal(TimeSpan.FromSeconds(1), time.DueTime);

        time.Fire();
        await opening;
        Assert.False(visible);
        await presentation.UpdateAsync(tab, false);
        Assert.True(visible);
    }

    [AvaloniaFact]
    public void Overlay_cancels_pending_activation_focus_and_blocks_new_requests()
    {
        var host = new WebTerminalHostControl();
        host.FocusAfterWindowActivation();
        host.IsInteractionBlocked = true;
        host.FocusAfterWindowActivation();
        Assert.False(host.IsHitTestVisible);
        var pending = typeof(WebTerminalHostControl).GetField(
            "focusAfterActivationPending", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        Assert.False((bool)pending.GetValue(host)!);
        host.IsInteractionBlocked = false;
        Assert.True(host.IsHitTestVisible);
        host.PrepareForRemoval();
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private TimerCallback? callback;
        private object? timerState;
        public TimeSpan DueTime { get; private set; }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            this.callback = callback;
            timerState = state;
            DueTime = dueTime;
            return new ManualTimer();
        }

        public void Fire() => callback!(timerState);

        private sealed class ManualTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private static WriteableBitmap CreatePreview() =>
        new(new PixelSize(8, 8), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
}
