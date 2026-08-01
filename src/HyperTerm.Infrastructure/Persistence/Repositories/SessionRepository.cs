using Microsoft.EntityFrameworkCore;
using HyperTerm.Core.Abstractions.Persistence;
using HyperTerm.Core.Entities;

namespace HyperTerm.Infrastructure.Persistence.Repositories;

internal sealed class SessionRepository(
    IDbContextFactory<HyperTermDbContext> contextFactory) : ISessionRepository
{
    public async Task<IReadOnlyList<Session>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        await using HyperTermDbContext context =
            await contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.Sessions
            .AsNoTracking()
            .OrderBy(session => session.Folder)
            .ThenBy(session => session.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<Session?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        await using HyperTermDbContext context =
            await contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.Sessions
            .AsNoTracking()
            .SingleOrDefaultAsync(session => session.Id == id, cancellationToken);
    }

    public async Task AddAsync(
        Session session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        await using HyperTermDbContext context =
            await contextFactory.CreateDbContextAsync(cancellationToken);

        context.Sessions.Add(session);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(
        Session session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        await using HyperTermDbContext context =
            await contextFactory.CreateDbContextAsync(cancellationToken);

        context.Sessions.Update(session);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> DeleteAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        await using HyperTermDbContext context =
            await contextFactory.CreateDbContextAsync(cancellationToken);

        int affectedRows = await context.Sessions
            .Where(session => session.Id == id)
            .ExecuteDeleteAsync(cancellationToken);

        return affectedRows > 0;
    }
}
