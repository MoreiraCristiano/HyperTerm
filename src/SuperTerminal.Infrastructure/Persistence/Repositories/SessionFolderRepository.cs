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

    public async Task<bool> RenameTreeAsync(
        string currentPath,
        string newPath,
        CancellationToken cancellationToken = default)
    {
        await using SuperTerminalDbContext context =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        string currentPrefix = $"{currentPath}/";
        List<SessionFolder> folders = await context.SessionFolders
            .Where(folder => folder.Path == currentPath || folder.Path.StartsWith(currentPrefix))
            .ToListAsync(cancellationToken);
        if (folders.Count == 0)
        {
            return false;
        }

        string[] replacementPaths = folders
            .Select(folder => ReplaceRoot(folder.Path, currentPath, newPath))
            .ToArray();
        bool collides = await context.SessionFolders.AnyAsync(
            folder => replacementPaths.Contains(folder.Path) &&
                !(folder.Path == currentPath || folder.Path.StartsWith(currentPrefix)),
            cancellationToken);
        if (collides)
        {
            throw new InvalidOperationException("The destination folder already exists.");
        }

        List<Session> sessions = await context.Sessions
            .Where(session => session.Folder == currentPath || session.Folder.StartsWith(currentPrefix))
            .ToListAsync(cancellationToken);

        foreach (SessionFolder folder in folders)
        {
            context.Entry(folder).Property(item => item.Path).CurrentValue =
                ReplaceRoot(folder.Path, currentPath, newPath);
        }

        foreach (Session session in sessions)
        {
            context.Entry(session).Property(item => item.Folder).CurrentValue =
                ReplaceRoot(session.Folder, currentPath, newPath);
            context.Entry(session).Property(item => item.UpdatedAt).CurrentValue = DateTime.UtcNow;
        }

        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private static string ReplaceRoot(string path, string currentPath, string newPath) =>
        path.Equals(currentPath, StringComparison.OrdinalIgnoreCase)
            ? newPath
            : $"{newPath}{path[currentPath.Length..]}";
}
