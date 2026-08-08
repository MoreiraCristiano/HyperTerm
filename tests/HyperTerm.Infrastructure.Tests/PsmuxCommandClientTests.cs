using HyperTerm.Core.Exceptions;
using HyperTerm.Infrastructure.Terminal;

namespace HyperTerm.Infrastructure.Tests;

public sealed class PsmuxCommandClientTests
{
    [Fact]
    public async Task CommandTimeoutTerminatesTheControlProcess()
    {
        var client = new PsmuxCommandClient();
        string helperAssembly = typeof(HyperTerm.TestTerminal.AssemblyMarker).Assembly.Location;

        TerminalLaunchException exception = await Assert.ThrowsAsync<TerminalLaunchException>(
            () => client.RunAsync(
                "dotnet.exe",
                [helperAssembly, "hang"],
                TimeSpan.FromMilliseconds(250),
                TestContext.Current.CancellationToken));

        Assert.Contains("did not respond", exception.Message);
    }

    [Fact]
    public async Task CallerCancellationIsPreservedAfterTerminatingTheControlProcess()
    {
        var client = new PsmuxCommandClient();
        string helperAssembly = typeof(HyperTerm.TestTerminal.AssemblyMarker).Assembly.Location;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(250));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.RunAsync(
                "dotnet.exe",
                [helperAssembly, "hang"],
                TimeSpan.FromSeconds(10),
                cancellation.Token));
    }
}
