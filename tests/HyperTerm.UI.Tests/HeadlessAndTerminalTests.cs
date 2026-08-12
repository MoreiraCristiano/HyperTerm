using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using HyperTerm.UI.Controls;
using HyperTerm.UI.Views.Dialogs;

namespace HyperTerm.UI.Tests;

public sealed class AvaloniaHeadlessTests
{
    [AvaloniaFact]
    [Trait("Category", "Headless")]
    public void Headless_application_is_initialized()
    {
        Avalonia.Application? application = Avalonia.Application.Current;
        Assert.NotNull(application);
        Assert.NotNull(application!.Resources);
    }

    [AvaloniaFact]
    [Trait("Category", "Headless")]
    public void Controls_measure_and_focus_on_headless_dispatcher()
    {
        var textBox = new TextBox { Text = "HyperTerm", MinWidth = 100 };
        var window = new Window
        {
            Width = 900,
            Height = 600,
            Content = textBox,
        };

        window.Show();
        textBox.Focus();

        Assert.True(textBox.IsFocused);
        Assert.Equal("HyperTerm", textBox.Text);
        window.Close();
    }

    [AvaloniaFact]
    [Trait("Category", "Headless")]
    public void Command_palette_focuses_query_after_becoming_visible()
    {
        var palette = new CommandPaletteDialog
        {
            IsVisible = false,
        };
        var window = new Window
        {
            Width = 900,
            Height = 600,
            Content = palette,
        };
        window.Show();

        palette.IsVisible = true;
        Dispatcher.UIThread.RunJobs();

        TextBox query = palette.FindControl<TextBox>("QueryEditor")!;
        Assert.True(query.IsFocused);
        window.Close();
    }

    [AvaloniaFact]
    [Trait("Category", "Headless")]
    public void Settings_use_vertical_tabs_with_independent_scrolling()
    {
        var dialog = new SettingsDialog { IsVisible = true };
        var window = new Window
        {
            Width = 1100,
            Height = 760,
            Content = dialog,
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        TabControl tabs = dialog.FindControl<TabControl>("SettingsTabs")!;
        TabItem[] items = tabs.Items.OfType<TabItem>().ToArray();
        Assert.Equal(Dock.Left, tabs.TabStripPlacement);
        Assert.Equal(5, items.Length);
        Assert.All(items, item => Assert.IsType<ScrollViewer>(item.Content));
        Assert.All(items, item => Assert.Contains("settingsTab", item.Classes));
        ComboBox fontFamilyPicker = dialog.FindControl<ComboBox>("FontFamilyPicker")!;
        Assert.Equal(360, fontFamilyPicker.Width);
        Assert.False(fontFamilyPicker.IsEditable);

        tabs.SelectedIndex = 2;
        dialog.IsVisible = false;
        dialog.IsVisible = true;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(2, tabs.SelectedIndex);
        Assert.Same(items[2], tabs.SelectedItem);
        window.Close();
    }

    [AvaloniaFact]
    [Trait("Category", "Headless")]
    public void Settings_tab_headers_keep_geometry_when_selection_changes()
    {
        var first = new TabItem { Header = "General", Content = new Border() };
        var second = new TabItem { Header = "Terminal", Content = new Border() };
        first.Classes.Add("settingsTab");
        second.Classes.Add("settingsTab");
        var tabHeaders = new StackPanel
        {
            Width = 600,
            Height = 400,
            Children = { first, second },
        };
        first.IsSelected = true;
        var window = new Window
        {
            Width = 600,
            Height = 400,
            Content = tabHeaders,
        };
        window.Styles.Add(new FluentTheme { DensityStyle = DensityStyle.Compact });
        window.Styles.Add(new StyleInclude((Uri?)null)
        {
            Source = new Uri("avares://HyperTerm/Styles/DesignSystem.axaml"),
        });
        window.Show();
        tabHeaders.Measure(new Avalonia.Size(600, 400));
        tabHeaders.Arrange(new Avalonia.Rect(0, 0, 600, 400));
        Dispatcher.UIThread.RunJobs();

        (double Y, double Height)[] initialGeometry =
        [
            (first.Bounds.Y, first.Bounds.Height),
            (second.Bounds.Y, second.Bounds.Height),
        ];
        Assert.All(initialGeometry, geometry => Assert.Equal(28, geometry.Height));

        first.IsSelected = false;
        second.IsSelected = true;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(initialGeometry[0], (first.Bounds.Y, first.Bounds.Height));
        Assert.Equal(initialGeometry[1], (second.Bounds.Y, second.Bounds.Height));
        window.Close();
    }
}

public sealed class TerminalOutputBufferTests
{
    [Fact]
    public void High_volume_output_drains_in_bounded_batches_without_loss()
    {
        const int maximumBufferSize = 2 * 1024 * 1024;
        const int maximumBatchSize = 128 * 1024;
        const int chunkSize = 1024;
        var buffer = new TerminalOutputBuffer(maximumBufferSize, maximumBatchSize);
        string chunk = new('x', chunkSize);
        for (int index = 0; index < maximumBufferSize / chunkSize; index++)
        {
            Assert.True(buffer.Enqueue(chunk));
        }

        int drainedCharacters = 0;
        string? batch;
        while ((batch = buffer.TryDrainBatch()) is not null)
        {
            Assert.InRange(batch.Length, 1, maximumBatchSize);
            Assert.All(batch, character => Assert.Equal('x', character));
            drainedCharacters += batch.Length;
        }

        Assert.Equal(maximumBufferSize, drainedCharacters);
    }

    [Fact]
    public void Constructor_rejects_non_positive_limits()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TerminalOutputBuffer(0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TerminalOutputBuffer(1, 0));
    }

    [Fact]
    public void Drain_preserves_order_and_batch_boundary()
    {
        var buffer = new TerminalOutputBuffer(100, 5);
        Assert.True(buffer.Enqueue("ab"));
        Assert.True(buffer.Enqueue("cd"));
        Assert.True(buffer.Enqueue("ef"));

        Assert.Equal("abcd", buffer.TryDrainBatch());
        Assert.Equal("ef", buffer.TryDrainBatch());
        Assert.Null(buffer.TryDrainBatch());
    }

    [Fact]
    public async Task Complete_rejects_late_output_and_unblocks_writer()
    {
        var buffer = new TerminalOutputBuffer(3, 3);
        Assert.True(buffer.Enqueue("abc"));
        Task<bool> blockedWriter = Task.Run(() => buffer.Enqueue("d"));
        Assert.False(blockedWriter.IsCompleted);

        buffer.Complete();

        Assert.False(await blockedWriter.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.False(buffer.Enqueue("late"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("{\"type\":\"input\",\"tabId\":\"bad\",\"data\":\"x\"}")]
    [InlineData("{\"type\":\"resize\",\"tabId\":\"00000000000000000000000000000001\",\"columns\":0,\"rows\":24}")]
    public void Web_messages_reject_invalid_payloads(string? body) =>
        Assert.False(WebTerminalMessage.TryParse(body, out _));

    [Fact]
    public void Web_messages_preserve_raw_control_input()
    {
        const string body = "{\"type\":\"input\",\"tabId\":\"00000000000000000000000000000001\",\"data\":\"\\u0003\"}";

        Assert.True(WebTerminalMessage.TryParse(body, out WebTerminalMessage? message));
        Assert.Equal("\u0003", message!.Data);
    }
}
