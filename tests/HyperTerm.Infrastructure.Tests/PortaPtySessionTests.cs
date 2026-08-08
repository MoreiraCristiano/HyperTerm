using System.Text;
using HyperTerm.Core.Abstractions.Terminal;
using HyperTerm.Core.Models;
using HyperTerm.Infrastructure.Terminal;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HyperTerm.Infrastructure.Tests;

public sealed class PortaPtySessionTests
{
    [Fact]
    public async Task PreservesUtf8AcrossReadsAndToleratesOperationsAfterExit()
    {
        var factory = new PortaPtySessionFactory(
            NullLogger<PortaPtySessionFactory>.Instance);
        string expectedSuffix = "🙂end";
        string command =
            "[Console]::OutputEncoding=[System.Text.UTF8Encoding]::new($false);" +
            "[Console]::Out.Write(('x'*65535)+'🙂end');exit 7";
        var definition = new TerminalSessionDefinition(
            "powershell.exe",
            ["-NoLogo", "-NoProfile", "-NonInteractive", "-Command", command],
            Path.GetTempPath());
        await using IPtySession session = await factory.CreateAsync(
            definition,
            120,
            30,
            TestContext.Current.CancellationToken);
        var output = new StringBuilder();
        var outputCompleted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        session.OutputReceived += (_, chunk) =>
        {
            lock (output)
            {
                output.Append(chunk);
                if (output.ToString().Contains(expectedSuffix, StringComparison.Ordinal))
                {
                    outputCompleted.TrySetResult();
                }
            }
        };

        int exitCode = await session.Completion.WaitAsync(
            TimeSpan.FromSeconds(10),
            TestContext.Current.CancellationToken);
        await outputCompleted.Task.WaitAsync(
            TimeSpan.FromSeconds(10),
            TestContext.Current.CancellationToken);
        await session.WriteAsync("ignored", TestContext.Current.CancellationToken);
        session.Resize(80, 24);
        await session.DisposeAsync();
        await session.DisposeAsync();

        Assert.Equal(7, exitCode);
        Assert.Equal(TerminalSessionState.Disposed, session.State);
        lock (output)
        {
            Assert.Contains(expectedSuffix, output.ToString());
            Assert.DoesNotContain('\uFFFD', output.ToString());
        }
    }
}
