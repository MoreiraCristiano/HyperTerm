using Microsoft.EntityFrameworkCore;
using HyperTerm.Core.Abstractions.Persistence;
using HyperTerm.Core.Entities;
using HyperTerm.Core.Models;

namespace HyperTerm.Infrastructure.Persistence.Repositories;

internal sealed class SessionFolderRepository(
    IDbContextFactory<HyperTermDbContext> contextFactory) : ISessionFolderRepository
{
    public async Task<IReadOnlyList<SessionFolder>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        await using HyperTermDbContext context =
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
        await using HyperTermDbContext context =
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

        await using HyperTermDbContext context =
            await contextFactory.CreateDbContextAsync(cancellationToken);

        context.SessionFolders.Add(folder);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<FolderDeleteResult> DeleteTreesAsync(
        IReadOnlyCollection<string> paths,
        bool force,
        CancellationToken cancellationToken = default)
    {
        await using HyperTermDbContext context =
            await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        List<SessionFolder> allFolders = await context.SessionFolders
            .ToListAsync(cancellationToken);
        List<SessionFolder> foldersToDelete = allFolders
            .Where(folder => IsInsideAny(folder.Path, paths))
            .ToList();
        List<Session> sessionsToDelete = await context.Sessions
            .Where(session => session.Folder != string.Empty)
            .ToListAsync(cancellationToken);
        sessionsToDelete = sessionsToDelete
            .Where(session => IsInsideAny(session.Folder, paths))
            .ToList();

        if (foldersToDelete.Count == 0 && sessionsToDelete.Count == 0)
        {
            throw new KeyNotFoundException("The selected folders were not found.");
        }

        if (sessionsToDelete.Count > 0 && !force)
        {
            throw new InvalidOperationException(
                "Selected folders contain sessions. Enable force delete to remove them.");
        }

        if (force)
        {
            context.Sessions.RemoveRange(sessionsToDelete);
        }

        context.SessionFolders.RemoveRange(foldersToDelete);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new FolderDeleteResult(foldersToDelete.Count, sessionsToDelete.Count);
    }

    public async Task<bool> RenameTreeAsync(
        string currentPath,
        string newPath,
        CancellationToken cancellationToken = default)
    {
        await using HyperTermDbContext context =
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

    private static bool IsInsideAny(string path, IReadOnlyCollection<string> roots)
    {
        string normalizedPath = path.Replace('\\', '/').Trim('/');
        return roots.Any(root =>
            normalizedPath.Equals(root, StringComparison.OrdinalIgnoreCase) ||
            normalizedPath.StartsWith($"{root}/", StringComparison.OrdinalIgnoreCase));
    }
}
