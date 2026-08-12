namespace HyperTerm.Core.Abstractions.Terminal;

public interface ITerminalProfileResolver
{
    string? TryResolve(string configuredPath);

    string Resolve(string configuredPath);
}
