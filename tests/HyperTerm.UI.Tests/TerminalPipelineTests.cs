using System.Text.Json;
using HyperTerm.Core.Models;
using HyperTerm.UI.Controls;
using HyperTerm.UI.ViewModels;
using Xunit;

namespace HyperTerm.UI.Tests;

public sealed class TerminalPipelineTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-json")]
    [InlineData("{}")]
    [InlineData("{\"type\":\"unknown\"}")]
    public void RejectsInvalidWebMessages(string? body) =>
        Assert.False(WebTerminalMessage.TryParse(body, out _));

    [Fact]
    public void ParsesValidatedInputMessage()
    {
        Guid tabId = Guid.NewGuid();
        string body = JsonSerializer.Serialize(new
        {
            type = "input",
            tabId = tabId.ToString("N"),
            data = "echo test\r",
        });

        Assert.True(WebTerminalMessage.TryParse(body, out WebTerminalMessage? message));
        Assert.Equal("input", message!.Type);
        Assert.Equal(tabId, message.TabId);
        Assert.Equal("echo test\r", message.Data);
    }

    [Theory]
    [InlineData(0, 24)]
    [InlineData(80, 0)]
    [InlineData(10001, 24)]
    public void RejectsInvalidTerminalDimensions(int columns, int rows)
    {
        string body = JsonSerializer.Serialize(new
        {
            type = "resize",
            tabId = Guid.NewGuid().ToString("N"),
            columns,
            rows,
        });

        Assert.False(WebTerminalMessage.TryParse(body, out _));
    }

    [Fact]
    public void RejectsUnknownApplicationCommand()
    {
        string body = JsonSerializer.Serialize(new
        {
            type = "applicationCommand",
            tabId = Guid.NewGuid().ToString("N"),
            command = "deleteEverything",
        });

        Assert.False(WebTerminalMessage.TryParse(body, out _));
    }

    [Fact]
    public void ParsesPaneAwareResizeAndActivation()
    {
        Guid tabId = Guid.NewGuid();
        Guid paneId = Guid.NewGuid();
        string resize = JsonSerializer.Serialize(new
        {
            type = "resize",
            tabId = tabId.ToString("N"),
            paneId = paneId.ToString("N"),
            columns = 120,
            rows = 40,
        });
        string activation = JsonSerializer.Serialize(new
        {
            type = "paneActivated",
            tabId = tabId.ToString("N"),
            paneId = paneId.ToString("N"),
        });

        Assert.True(WebTerminalMessage.TryParse(resize, out WebTerminalMessage? resized));
        Assert.Equal(paneId, resized!.PaneId);
        Assert.True(WebTerminalMessage.TryParse(activation, out WebTerminalMessage? activated));
        Assert.Equal(paneId, activated!.PaneId);
    }

    [Theory]
    [InlineData("newTerminal")]
    [InlineData("searchTerminal")]
    [InlineData("commandPalette")]
    public void ParsesSupportedDiscoveryCommands(string command)
    {
        string body = JsonSerializer.Serialize(new
        {
            type = "applicationCommand",
            tabId = Guid.NewGuid().ToString("N"),
            command,
        });

        Assert.True(WebTerminalMessage.TryParse(body, out WebTerminalMessage? message));
        Assert.Equal(command, message!.Command);
    }

    [Fact]
    public void OutputBufferPreservesOrderAndBatchLimit()
    {
        var buffer = new TerminalOutputBuffer(32, 6);

        Assert.True(buffer.Enqueue("abc"));
        Assert.True(buffer.Enqueue("def"));
        Assert.True(buffer.Enqueue("gh"));

        Assert.Equal("abcdef", buffer.TryDrainBatch());
        Assert.Equal("gh", buffer.TryDrainBatch());
        Assert.False(buffer.HasData);
    }

    [Fact]
    public void CompletedOutputBufferRejectsNewOutput()
    {
        var buffer = new TerminalOutputBuffer(32, 16);
        buffer.Complete();

        Assert.False(buffer.Enqueue("ignored"));
        Assert.Null(buffer.TryDrainBatch());
    }

    [Fact]
    public async Task TerminalTabStartsOnceAndDisposesOnce()
    {
        var factory = new FakePtySessionFactory();
        TerminalTabViewModel tab = CreateTab(factory);

        await Task.WhenAll(
            tab.StartPtyAsync(80, 24, TestContext.Current.CancellationToken),
            tab.StartPtyAsync(100, 30, TestContext.Current.CancellationToken));
        await tab.DisposeAsync();
        await tab.DisposeAsync();

        Assert.Equal(1, factory.CreateCount);
        Assert.Equal(1, factory.LastSession!.DisposeCount);
    }

    [Fact]
    public async Task TerminalTabForwardsRawInputAndStopsAfterExit()
    {
        var factory = new FakePtySessionFactory();
        TerminalTabViewModel tab = CreateTab(factory);
        await tab.StartPtyAsync(80, 24, TestContext.Current.CancellationToken);

        await tab.WritePtyAsync("\u0003", TestContext.Current.CancellationToken);
        factory.LastSession!.RaiseExit(130);
        await tab.WritePtyAsync("ignored", TestContext.Current.CancellationToken);

        Assert.Equal(["\u0003"], factory.LastSession.Writes);
        Assert.Equal(130, await factory.LastSession.Completion);
        await tab.DisposeAsync();
    }

    [Fact]
    public async Task ClosingPaneDisposesOnlyItsSessionOnce()
    {
        var factory = new FakePtySessionFactory();
        TerminalTabViewModel tab = CreateTab(factory);
        await tab.StartPtyAsync(80, 24, TestContext.Current.CancellationToken);
        TerminalPaneViewModel second = Assert.IsType<TerminalPaneViewModel>(
            tab.SplitActivePane(SplitOrientation.Vertical, tab.Definition));
        await tab.StartPaneAsync(
            second.PaneId,
            80,
            24,
            TestContext.Current.CancellationToken);

        await tab.CloseActivePaneAsync();

        Assert.Equal(0, factory.Sessions[0].DisposeCount);
        Assert.Equal(1, factory.Sessions[1].DisposeCount);
        Assert.Single(tab.Panes);
        await tab.DisposeAsync();
        Assert.Equal(1, factory.Sessions[0].DisposeCount);
        Assert.Equal(1, factory.Sessions[1].DisposeCount);
    }

    [Fact]
    public async Task ClosingTabDisposesEveryPaneExactlyOnce()
    {
        var factory = new FakePtySessionFactory();
        TerminalTabViewModel tab = CreateTab(factory);
        await tab.StartPtyAsync(80, 24, TestContext.Current.CancellationToken);
        TerminalPaneViewModel second = tab.SplitActivePane(
            SplitOrientation.Horizontal,
            tab.Definition)!;
        await tab.StartPaneAsync(
            second.PaneId,
            80,
            24,
            TestContext.Current.CancellationToken);

        await tab.DisposeAsync();
        await tab.DisposeAsync();

        Assert.Equal(2, factory.Sessions.Count);
        Assert.All(factory.Sessions, session => Assert.Equal(1, session.DisposeCount));
    }

    private static TerminalTabViewModel CreateTab(FakePtySessionFactory factory) =>
        new(
            "Test",
            new TerminalSessionDefinition(
                "pwsh.exe",
                [],
                string.Empty,
                TerminalSessionKind.PowerShell),
            factory,
            "Cascadia Mono",
            13,
            "#264F78",
            "Bar",
            true,
            "Default Dark",
            _ => Task.CompletedTask);
}
