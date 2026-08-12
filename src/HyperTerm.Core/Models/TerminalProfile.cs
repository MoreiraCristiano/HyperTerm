namespace HyperTerm.Core.Models;

public sealed record TerminalProfile
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required string ExecutablePath { get; init; }

    public IReadOnlyList<string> Arguments { get; init; } = [];

    public string StartingDirectory { get; init; } = string.Empty;
}

public static class TerminalProfileIds
{
    public const string PowerShell = "powershell";
}
