using HyperTerm.Infrastructure.Terminal;
using Xunit;

namespace HyperTerm.Tests;

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

    private static string CreateTemporaryDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "HyperTerm.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
