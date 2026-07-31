using SuperTerminal.Core.Entities;

namespace SuperTerminal.Core.Abstractions.Services;

public interface ISessionFolderService
{
    Task<IReadOnlyList<SessionFolder>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<SessionFolder> CreateAsync(string path, CancellationToken cancellationToken = default);

    Task RenameAsync(
        string currentPath,
        string newPath,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(string path, CancellationToken cancellationToken = default);
}
