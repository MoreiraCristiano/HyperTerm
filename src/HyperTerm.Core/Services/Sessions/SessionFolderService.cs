using HyperTerm.Core.Abstractions.Persistence;
using HyperTerm.Core.Abstractions.Services;
using HyperTerm.Core.Entities;
using HyperTerm.Core.Models;

namespace HyperTerm.Core.Services;

internal sealed class SessionFolderService(ISessionFolderRepository repository)
    : ISessionFolderService
{
    public Task<IReadOnlyList<SessionFolder>> GetAllAsync(
        CancellationToken cancellationToken = default) =>
        repository.GetAllAsync(cancellationToken);

    public async Task<SessionFolder> CreateAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        string normalizedPath = SessionFolderPath.Normalize(path);
        if (await repository.ExistsAsync(normalizedPath, cancellationToken))
        {
            throw new InvalidOperationException($"Folder ‘{normalizedPath}’ already exists.");
        }

        var folder = new SessionFolder(Guid.NewGuid(), normalizedPath, DateTime.UtcNow);
        await repository.AddAsync(folder, cancellationToken);
        return folder;
    }

    public Task<FolderDeleteResult> DeleteAsync(
        IReadOnlyCollection<string> paths,
        bool force,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (paths.Count == 0)
        {
            throw new ArgumentException("Select at least one folder.", nameof(paths));
        }

        string[] normalizedPaths = paths
            .Select(SessionFolderPath.Normalize)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        string[] roots = normalizedPaths
            .Where(path => !normalizedPaths.Any(other =>
                !path.Equals(other, StringComparison.OrdinalIgnoreCase) &&
                    path.StartsWith($"{other}/", StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        return repository.DeleteTreesAsync(roots, force, cancellationToken);
    }

    public async Task RenameAsync(
        string currentPath,
        string newPath,
        CancellationToken cancellationToken = default)
    {
        string normalizedCurrentPath = SessionFolderPath.Normalize(currentPath);
        string normalizedNewPath = SessionFolderPath.Normalize(newPath);
        if (normalizedCurrentPath.Equals(normalizedNewPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (normalizedNewPath.StartsWith(
                $"{normalizedCurrentPath}/",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("A folder cannot be moved inside itself.");
        }

        bool renamed = await repository.RenameTreeAsync(
            normalizedCurrentPath,
            normalizedNewPath,
            cancellationToken);
        if (!renamed)
        {
            throw new KeyNotFoundException($"Folder ‘{normalizedCurrentPath}’ was not found.");
        }
    }

}
