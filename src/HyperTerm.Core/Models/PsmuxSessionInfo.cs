namespace HyperTerm.Core.Models;

public sealed record PsmuxSessionInfo(
    string Name,
    int WindowCount,
    bool IsAttached);

public sealed record PsmuxAvailability(
    bool IsAvailable,
    string? ExecutablePath,
    string? Version,
    string? Error);
