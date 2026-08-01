namespace HyperTerm.Core.Models;

public sealed record TerminalSessionDefinition(
    string Process,
    IReadOnlyList<string> Arguments,
    string StartingDirectory);
