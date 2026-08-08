using HyperTerm.Core.Abstractions.Persistence;
using HyperTerm.Core.Entities;
using HyperTerm.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace HyperTerm.Infrastructure.Persistence.Repositories;

internal sealed class SessionImportRepository(
    IDbContextFactory<HyperTermDbContext> contextFactory) : ISessionImportRepository
{
    public async Task<SessionImportSnapshot> GetSnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        await using HyperTermDbContext context =
            await contextFactory.CreateDbContextAsync(cancellationToken);

        List<SessionFolder> folders = await context.SessionFolders
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        List<Session> sessions = await context.Sessions
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return new SessionImportSnapshot(folders, sessions);
    }

    public async Task ApplyAsync(
        IReadOnlyCollection<SessionFolder> addedFolders,
        IReadOnlyCollection<Session> addedSessions,
        IReadOnlyCollection<Session> updatedSessions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(addedFolders);
        ArgumentNullException.ThrowIfNull(addedSessions);
        ArgumentNullException.ThrowIfNull(updatedSessions);

        if (addedFolders.Count == 0 &&
            addedSessions.Count == 0 &&
            updatedSessions.Count == 0)
        {
            return;
        }

        await using HyperTermDbContext context =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(
            cancellationToken);

        context.SessionFolders.AddRange(addedFolders);
        context.Sessions.AddRange(addedSessions);
        context.Sessions.UpdateRange(updatedSessions);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
