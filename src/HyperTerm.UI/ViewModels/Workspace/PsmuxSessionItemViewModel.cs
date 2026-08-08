using HyperTerm.Core.Models;

namespace HyperTerm.UI.ViewModels;

public sealed class PsmuxSessionItemViewModel(PsmuxSessionInfo session)
{
    public string Name { get; } = session.Name;

    public int WindowCount { get; } = session.WindowCount;

    public bool IsAttached { get; } = session.IsAttached;

    public string Details =>
        $"{WindowCount} {(WindowCount == 1 ? "window" : "windows")}" +
        (IsAttached ? " · attached" : " · detached");
}
