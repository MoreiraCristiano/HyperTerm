using HyperTerm.Infrastructure.Terminal;
using Xunit;

namespace HyperTerm.Tests;

public sealed class PsmuxServiceTests
{
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
}
