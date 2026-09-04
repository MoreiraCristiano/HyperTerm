namespace HyperTerm.Core.Models;

public sealed record TerminalSessionDefinition(
    string Process,
    IReadOnlyList<string> Arguments,
    string StartingDirectory,
    TerminalSessionKind Kind = TerminalSessionKind.Local)
{
    public string? ProfileId { get; init; }

    public string? DisplayName { get; init; }
}

public enum TerminalSessionKind
{
    Local,
    PowerShell,
    Ssh,
}
