using System.Text;
using HyperTerm.Core.Abstractions.Terminal;
using HyperTerm.Core.Models;
using HyperTerm.Infrastructure.Terminal;
using Microsoft.Extensions.Logging.Abstractions;

namespace HyperTerm.Infrastructure.Tests;

public sealed class PtyLifecycleTests
{
    [Fact]
    [Trait("Category", "ConPty")]
    public async Task Normal_exit_delivers_output_and_replays_exit_to_late_subscriber()
    {
        var factory = CreateFactory();
        await using IPtySession session = await factory.CreateAsync(
            PowerShell("[Console]::Out.Write('READY'); exit 23"),
            80,
            24,
            TestContext.Current.CancellationToken);
        var output = new StringBuilder();
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.OutputReceived += (_, chunk) =>
        {
            lock (output)
            {
                output.Append(chunk);
                if (output.ToString().Contains("READY", StringComparison.Ordinal))
                {
                    ready.TrySetResult();
                }
            }
        };

        int exitCode = await session.Completion.WaitAsync(
            TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        var replayed = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Exited += (_, code) => replayed.TrySetResult(code);

        Assert.Equal(23, exitCode);
        Assert.Equal(23, await replayed.Task.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal(TerminalSessionState.Exited, session.State);
    }

    [Fact]
    [Trait("Category", "ConPty")]
    public async Task Canceled_creation_does_not_leave_a_session()
    {
        var factory = CreateFactory();
        using var source = new CancellationTokenSource();
        source.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            factory.CreateAsync(PowerShell("exit 0"), 80, 24, source.Token));
    }

    [Fact]
    [Trait("Category", "ConPty")]
    public async Task Deterministic_helper_propagates_nonzero_exit_code()
    {
        string helperAssembly = typeof(HyperTerm.TestTerminal.AssemblyMarker).Assembly.Location;
        var definition = new TerminalSessionDefinition(
            "dotnet.exe",
            [helperAssembly, "exit", "19"],
            Path.GetDirectoryName(helperAssembly)!);
        var factory = CreateFactory();

        await using IPtySession session = await factory.CreateAsync(
            definition, 80, 24, TestContext.Current.CancellationToken);

        Assert.Equal(19, await session.Completion.WaitAsync(
            TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
    }

    [Fact]
    [Trait("Category", "Stress")]
    public async Task Repeated_session_creation_and_exit_remains_stable()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("HYPERTERM_RUN_STRESS"),
                "1",
                StringComparison.Ordinal))
        {
            return;
        }

        var factory = CreateFactory();
        for (int iteration = 0; iteration < 100; iteration++)
        {
            await using IPtySession session = await factory.CreateAsync(
                PowerShell($"[Console]::Out.Write('{iteration}'); exit 0"),
                80,
                24,
                TestContext.Current.CancellationToken);
            Assert.Equal(0, await session.Completion.WaitAsync(
                TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        }
    }

    private static PortaPtySessionFactory CreateFactory() =>
        new(NullLogger<PortaPtySessionFactory>.Instance);

    private static TerminalSessionDefinition PowerShell(string command) => new(
        "powershell.exe",
        ["-NoLogo", "-NoProfile", "-NonInteractive", "-Command", command],
        Path.GetTempPath());
}
