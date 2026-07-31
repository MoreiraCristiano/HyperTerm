using SuperTerminal.Core.Abstractions.Persistence;
using SuperTerminal.Core.Abstractions.Services;
using SuperTerminal.Core.Entities;

namespace SuperTerminal.Core.Services;

internal sealed class SessionFolderService(
    ISessionFolderRepository repository,
    ISessionRepository sessionRepository)
    : ISessionFolderService
{
    public Task<IReadOnlyList<SessionFolder>> GetAllAsync(
        CancellationToken cancellationToken = default) =>
        repository.GetAllAsync(cancellationToken);

    public async Task<SessionFolder> CreateAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        string normalizedPath = NormalizePath(path);
        if (await repository.ExistsAsync(normalizedPath, cancellationToken))
        {
            throw new InvalidOperationException($"Folder ‘{normalizedPath}’ already exists.");
        }

        var folder = new SessionFolder(Guid.NewGuid(), normalizedPath, DateTime.UtcNow);
        await repository.AddAsync(folder, cancellationToken);
        return folder;
    }

    public async Task DeleteAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        string normalizedPath = NormalizePath(path);
        IReadOnlyList<Session> sessions = await sessionRepository.GetAllAsync(cancellationToken);
        bool containsSessions = sessions.Any(session =>
        {
            string sessionPath = session.Folder.Replace('\\', '/').Trim('/');
            return sessionPath.Equals(normalizedPath, StringComparison.OrdinalIgnoreCase) ||
                sessionPath.StartsWith($"{normalizedPath}/", StringComparison.OrdinalIgnoreCase);
        });

        if (containsSessions)
        {
            throw new InvalidOperationException(
                "Move or delete the sessions inside this folder first.");
        }

        int deleted = await repository.DeleteTreeAsync(normalizedPath, cancellationToken);
        if (deleted == 0)
        {
            throw new KeyNotFoundException($"Folder ‘{normalizedPath}’ was not found.");
        }
    }

    public async Task RenameAsync(
        string currentPath,
        string newPath,
        CancellationToken cancellationToken = default)
    {
        string normalizedCurrentPath = NormalizePath(currentPath);
        string normalizedNewPath = NormalizePath(newPath);
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

    private static string NormalizePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string[] segments = path
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (segments.Length == 0 || segments.Any(segment => segment is "." or ".."))
        {
            throw new ArgumentException("Enter a valid folder path.", nameof(path));
        }

        string normalized = string.Join('/', segments);
        if (normalized.Length > 500)
        {
            throw new ArgumentException("Folder path cannot exceed 500 characters.", nameof(path));
        }

        return normalized;
    }
}
