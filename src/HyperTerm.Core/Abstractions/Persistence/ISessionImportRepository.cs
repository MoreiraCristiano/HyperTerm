using HyperTerm.Core.Entities;
using HyperTerm.Core.Models;

namespace HyperTerm.Core.Abstractions.Persistence;

public interface ISessionImportRepository
{
    Task<SessionImportSnapshot> GetSnapshotAsync(
        CancellationToken cancellationToken = default);

    Task ApplyAsync(
        IReadOnlyCollection<SessionFolder> addedFolders,
        IReadOnlyCollection<Session> addedSessions,
        IReadOnlyCollection<Session> updatedSessions,
        CancellationToken cancellationToken = default);
}
