using HyperTerm.Core.Exceptions;
using HyperTerm.Core.Models;

namespace HyperTerm.Core.Tests;

public sealed class ModelTests
{
    [Fact]
    public void Import_result_totals_added_and_updated_sessions()
    {
        var result = new SessionImportResult(2, 3, 4);
        Assert.Equal(5, result.ImportedSessions);
        Assert.Equal(4, result.AddedFolders);
    }

    [Fact]
    public void Terminal_launch_exception_preserves_context()
    {
        var inner = new InvalidOperationException("inner");
        var simple = new TerminalLaunchException("simple");
        var wrapped = new TerminalLaunchException("wrapped", inner);

        Assert.Equal("simple", simple.Message);
        Assert.Equal("wrapped", wrapped.Message);
        Assert.Same(inner, wrapped.InnerException);
    }

    [Theory]
    [InlineData(50L, 100L, 50)]
    [InlineData(150L, 100L, 100)]
    [InlineData(-50L, 100L, 0)]
    [InlineData(50L, null, 0)]
    [InlineData(50L, 0L, 0)]
    public void Update_progress_reports_bounded_percentage(
        long received,
        long? total,
        int expected)
    {
        var progress = new ApplicationUpdateProgress(received, total);

        Assert.Equal(expected, progress.Percentage);
    }
}
