using HyperTerm.Core.Abstractions.Terminal;
using HyperTerm.Core.Models;
using HyperTerm.UI.Controls;
using HyperTerm.UI.ViewModels;

namespace HyperTerm.UI.Tests;

public sealed class WebTerminalScriptBridgeTests
{
    [Fact]
    public async Task Write_serializes_control_characters_without_changing_payload()
    {
        string? script = null;
        var bridge = new WebTerminalScriptBridge(value =>
        {
            script = value;
            return Task.CompletedTask;
        });
        Guid tabId = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");

        await bridge.WriteAsync(tabId, 42, "line\r\n'quoted'\u0003");

        Assert.Equal(
            "window.terminalHost.write(\"00112233445566778899aabbccddeeff\", 42, " +
            "\"line\\r\\n\\u0027quoted\\u0027\\u0003\")",
            script);
    }

    [Fact]
    public async Task Create_serializes_terminal_identity_and_appearance()
    {
        string? script = null;
        var bridge = new WebTerminalScriptBridge(value =>
        {
            script = value;
            return Task.CompletedTask;
        });
        TerminalTabViewModel tab = CreateTab();

        await bridge.CreateAsync(tab);

        Assert.NotNull(script);
        Assert.StartsWith("window.terminalHost.create(", script, StringComparison.Ordinal);
        Assert.Contains(tab.Id.ToString("N"), script, StringComparison.Ordinal);
        Assert.Contains("\"fontFamily\":\"Cascadia Mono\"", script, StringComparison.Ordinal);
        Assert.Contains("\"cursorStyle\":\"bar\"", script, StringComparison.Ordinal);
    }

    internal static TerminalTabViewModel CreateTab() => new(
        "Test",
        new TerminalSessionDefinition(
            "powershell.exe",
            [],
            Path.GetTempPath()),
        new UnusedPtySessionFactory(),
        "Cascadia Mono",
        14,
        "#123456",
        "Bar",
        true,
        _ => Task.CompletedTask);

    private sealed class UnusedPtySessionFactory : IPtySessionFactory
    {
        public Task<IPtySession> CreateAsync(
            TerminalSessionDefinition definition,
            int columns,
            int rows,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}

public sealed class TerminalOutputCoordinatorTests
{
    [Fact]
    public async Task Flush_prioritizes_active_terminal_and_limits_parallel_writes()
    {
        HostedTerminal[] terminals = Enumerable.Range(0, 5)
            .Select(_ => new HostedTerminal(WebTerminalScriptBridgeTests.CreateTab())
            {
                Created = true,
            })
            .ToArray();
        HostedTerminal active = terminals[^1];
        var writes = new List<(HostedTerminal Hosted, string Output, long Token)>();
        var coordinator = new TerminalOutputCoordinator(
            () => terminals,
            () => active,
            (hosted, output, token) =>
            {
                writes.Add((hosted, output, token));
                return Task.CompletedTask;
            },
            () => { });
        foreach (HostedTerminal hosted in terminals)
        {
            Assert.True(hosted.Output.Enqueue(hosted.Tab.Id.ToString("N")));
        }

        bool hasPendingOutput = await coordinator.FlushAsync();

        Assert.True(hasPendingOutput);
        Assert.Equal(4, writes.Count);
        Assert.Same(active, writes[0].Hosted);
        Assert.All(writes, write => Assert.Equal(1, write.Token));
    }

    [Fact]
    public async Task Rejected_acknowledgement_releases_write_and_reports_failure()
    {
        var hosted = new HostedTerminal(WebTerminalScriptBridgeTests.CreateTab())
        {
            Created = true,
        };
        int flushRequests = 0;
        var coordinator = new TerminalOutputCoordinator(
            () => [hosted],
            () => hosted,
            (_, _, _) => Task.CompletedTask,
            () => flushRequests++);
        Assert.True(coordinator.Enqueue(hosted, "output"));
        await coordinator.FlushAsync();

        coordinator.Acknowledge(hosted, hosted.WriteToken, success: false);

        Assert.False(hosted.WriteInFlight);
        Assert.Equal(2, flushRequests);
        Assert.Equal(
            "Launch failed: Terminal renderer rejected output.",
            hosted.Tab.ConnectionStatus);
    }

    [Fact]
    public async Task Timeout_is_injectable_and_stale_timeout_cannot_release_new_write()
    {
        var hosted = new HostedTerminal(WebTerminalScriptBridgeTests.CreateTab())
        {
            Created = true,
        };
        var delays = new Queue<TaskCompletionSource>();
        var coordinator = new TerminalOutputCoordinator(
            () => [hosted],
            () => hosted,
            (_, _, _) => Task.CompletedTask,
            () => { },
            (_, _) =>
            {
                var completion = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                delays.Enqueue(completion);
                return completion.Task;
            });
        Assert.True(hosted.Output.Enqueue("first"));
        await coordinator.FlushAsync();
        long firstToken = hosted.WriteToken;
        coordinator.Acknowledge(hosted, firstToken, success: true);
        Assert.True(hosted.Output.Enqueue("second"));
        await coordinator.FlushAsync();

        delays.Dequeue().TrySetResult();
        await Task.Yield();

        Assert.True(hosted.WriteInFlight);
        Assert.Equal(firstToken + 1, hosted.WriteToken);
        coordinator.Complete(hosted);
    }
}
