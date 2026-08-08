using HyperTerm.Core.Exceptions;
using HyperTerm.Infrastructure.Terminal;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HyperTerm.Infrastructure.Tests;

public sealed class PsmuxServiceTests
{
    [Fact]
    public void BundledPsmuxTakesPriorityOverPath()
    {
        string testRoot = CreateTemporaryDirectory();
        try
        {
            string applicationDirectory = Path.Combine(testRoot, "app with spaces");
            string bundledDirectory = Path.Combine(applicationDirectory, "tools", "psmux");
            string pathDirectory = Path.Combine(testRoot, "path");
            Directory.CreateDirectory(bundledDirectory);
            Directory.CreateDirectory(pathDirectory);
            string bundledExecutable = Path.Combine(bundledDirectory, "psmux.exe");
            File.WriteAllText(bundledExecutable, string.Empty);
            File.WriteAllText(Path.Combine(pathDirectory, "psmux.exe"), string.Empty);

            string? resolved = WindowsExecutableResolver.TryResolveBundledOrPath(
                Path.Combine("tools", "psmux", "psmux.exe"),
                "psmux.exe",
                "psmux.exe",
                applicationDirectory,
                [pathDirectory]);

            Assert.Equal(bundledExecutable, resolved);
        }
        finally
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }

    [Fact]
    public void MissingBundledPsmuxFallsBackToPath()
    {
        string testRoot = CreateTemporaryDirectory();
        try
        {
            string pathDirectory = Path.Combine(testRoot, "external tools with spaces");
            Directory.CreateDirectory(pathDirectory);
            string pathExecutable = Path.Combine(pathDirectory, "psmux.exe");
            File.WriteAllText(pathExecutable, string.Empty);

            string? resolved = WindowsExecutableResolver.TryResolveBundledOrPath(
                Path.Combine("tools", "psmux", "psmux.exe"),
                "psmux.exe",
                "psmux.exe",
                Path.Combine(testRoot, "app"),
                [pathDirectory]);

            Assert.Equal(pathExecutable, resolved);
        }
        finally
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }

    [Fact]
    public void MissingBundledAndPathPsmuxReturnsNull()
    {
        string testRoot = CreateTemporaryDirectory();
        try
        {
            string? resolved = WindowsExecutableResolver.TryResolveBundledOrPath(
                Path.Combine("tools", "psmux", "psmux.exe"),
                "psmux.exe",
                "psmux.exe",
                testRoot,
                []);

            Assert.Null(resolved);
        }
        finally
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }

    [Fact]
    public void PsmuxControlCommandsUseUserProfileAsStartingDirectory()
    {
        Assert.Equal(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            PsmuxService.GetDefaultStartingDirectory());
    }

    [Fact]
    public void NewSessionUsesDetachedCreateHorizontalSplitAndLeftFocusCommands()
    {
        IReadOnlyList<string> create =
            PsmuxService.BuildDetachedSessionArguments("work");
        IReadOnlyList<string> split =
            PsmuxService.BuildSplitArguments("work");
        IReadOnlyList<string> select =
            PsmuxService.BuildSelectLeftArguments("work");

        Assert.Equal(
            ["-L", "hyperterm", "new-session", "-d", "-s", "work"],
            create);
        Assert.Equal(
            ["-L", "hyperterm", "split-window", "-h", "-t", "work"],
            split);
        Assert.Equal(
            ["-L", "hyperterm", "select-pane", "-L", "-t", "work"],
            select);
    }

    [Fact]
    public void AttachSessionDoesNotChangeExistingLayout()
    {
        IReadOnlyList<string> arguments =
            PsmuxService.BuildAttachSessionArguments("work");

        Assert.Equal(
            ["-L", "hyperterm", "attach-session", "-t", "work"],
            arguments);
        Assert.DoesNotContain("split-window", arguments);
    }

    [Fact]
    public void StopServerCommandTargetsOnlyHyperTermNamespace()
    {
        Assert.Equal(
            ["-L", "hyperterm", "kill-server"],
            PsmuxService.BuildKillServerArguments());
    }

    [Fact]
    public async Task StopServerEndsNamespaceWhenAllSessionsAreDetached()
    {
        var commands = new FakePsmuxCommandClient(
            new PsmuxCommandResult(0, "work\t1\t0\n", string.Empty),
            new PsmuxCommandResult(0, string.Empty, string.Empty));
        var service = CreateService(commands);

        bool stopped = await service.TryStopServerAsync(
            TestContext.Current.CancellationToken);

        Assert.True(stopped);
        Assert.Equal(2, commands.Arguments.Count);
        Assert.Equal(
            ["-L", "hyperterm", "kill-server"],
            commands.Arguments[1]);
    }

    [Fact]
    public async Task StopServerPreservesNamespaceWhenAClientIsAttached()
    {
        var commands = new FakePsmuxCommandClient(
            new PsmuxCommandResult(0, "work\t1\t1\n", string.Empty));
        var service = CreateService(commands);

        bool stopped = await service.TryStopServerAsync(
            TestContext.Current.CancellationToken);

        Assert.False(stopped);
        Assert.Single(commands.Arguments);
    }

    [Fact]
    public async Task StopServerIsIdempotentWhenNoServerExists()
    {
        var commands = new FakePsmuxCommandClient(
            new PsmuxCommandResult(1, string.Empty, "no server running"),
            new PsmuxCommandResult(1, string.Empty, "no server running"));
        var service = CreateService(commands);

        bool stopped = await service.TryStopServerAsync(
            TestContext.Current.CancellationToken);

        Assert.True(stopped);
        Assert.Equal(2, commands.Arguments.Count);
    }

    [Fact]
    public async Task StopServerReportsUnexpectedCommandFailure()
    {
        var commands = new FakePsmuxCommandClient(
            new PsmuxCommandResult(0, "work\t1\t0\n", string.Empty),
            new PsmuxCommandResult(2, string.Empty, "access denied"));
        var service = CreateService(commands);

        TerminalLaunchException exception = await Assert.ThrowsAsync<TerminalLaunchException>(
            () => service.TryStopServerAsync(TestContext.Current.CancellationToken));

        Assert.Contains("access denied", exception.Message);
    }

    private static PsmuxService CreateService(IPsmuxCommandClient commandClient) =>
        new(NullLogger<PsmuxService>.Instance, commandClient);

    private static string CreateTemporaryDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "HyperTerm.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class FakePsmuxCommandClient(params PsmuxCommandResult[] results)
        : IPsmuxCommandClient
    {
        private readonly Queue<PsmuxCommandResult> results = new(results);

        public List<IReadOnlyList<string>> Arguments { get; } = [];

        public string? TryResolveExecutable() => @"C:\Tools\psmux.exe";

        public Task<PsmuxCommandResult> RunAsync(
            string executable,
            IReadOnlyList<string> arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Arguments.Add(arguments.ToArray());
            return Task.FromResult(results.Dequeue());
        }
    }
}
