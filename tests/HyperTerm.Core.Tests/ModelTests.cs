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

    [Fact]
    public void Psmux_availability_records_all_outcomes()
    {
        var available = new PsmuxAvailability(true, "psmux.exe", "1.0", null);
        var unavailable = new PsmuxAvailability(false, null, null, "missing");

        Assert.True(available.IsAvailable);
        Assert.Equal("psmux.exe", available.ExecutablePath);
        Assert.Equal("1.0", available.Version);
        Assert.Equal("missing", unavailable.Error);
    }
}
