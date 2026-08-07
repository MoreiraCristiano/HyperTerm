namespace HyperTerm.Core.Models;

public sealed record TerminalSessionDefinition(
    string Process,
    IReadOnlyList<string> Arguments,
    string StartingDirectory,
    TerminalSessionKind Kind = TerminalSessionKind.PowerShell,
    string? PsmuxSessionName = null);

public enum TerminalSessionKind
{
    PowerShell,
    Ssh,
    Psmux,
}
