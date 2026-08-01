namespace SuperTerminal.Core.Models;

public sealed record SessionImportResult(
    int AddedSessions,
    int UpdatedSessions,
    int AddedFolders)
{
    public int ImportedSessions => AddedSessions + UpdatedSessions;
}
