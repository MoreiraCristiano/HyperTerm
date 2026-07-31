using Microsoft.EntityFrameworkCore;
using SuperTerminal.Core.Abstractions.Persistence;
using SuperTerminal.Core.Entities;

namespace SuperTerminal.Infrastructure.Persistence.Repositories;

internal sealed class SessionFolderRepository(
    IDbContextFactory<SuperTerminalDbContext> contextFactory) : ISessionFolderRepository
{
    public async Task<IReadOnlyList<SessionFolder>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        await using SuperTerminalDbContext context =
            await contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.SessionFolders
            .AsNoTracking()
            .OrderBy(folder => folder.Path)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> ExistsAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        await using SuperTerminalDbContext context =
            await contextFactory.CreateDbContextAsync(cancellationToken);

        return await context.SessionFolders.AnyAsync(
            folder => folder.Path == path,
            cancellationToken);
    }

    public async Task AddAsync(
        SessionFolder folder,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(folder);

        await using SuperTerminalDbContext context =
            await contextFactory.CreateDbContextAsync(cancellationToken);

        context.SessionFolders.Add(folder);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> DeleteTreeAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        await using SuperTerminalDbContext context =
            await contextFactory.CreateDbContextAsync(cancellationToken);

        string childPrefix = $"{path}/";
        return await context.SessionFolders
            .Where(folder => folder.Path == path || folder.Path.StartsWith(childPrefix))
            .ExecuteDeleteAsync(cancellationToken);
    }
}
