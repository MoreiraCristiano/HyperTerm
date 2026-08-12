using HyperTerm.Core.Abstractions.Terminal;

namespace HyperTerm.Infrastructure.Terminal;

internal sealed class TerminalProfileResolver : ITerminalProfileResolver
{
    public string? TryResolve(string configuredPath)
    {
        string candidate = configuredPath.Trim().Trim('"');
        return candidate.Length == 0
            ? null
            : WindowsExecutableResolver.TryResolve(candidate, Path.GetFileName(candidate));
    }

    public string Resolve(string configuredPath)
    {
        string candidate = configuredPath.Trim().Trim('"');
        return WindowsExecutableResolver.Resolve(candidate, Path.GetFileName(candidate));
    }
}
