using SuperTerminal.Core.Entities;

namespace SuperTerminal.Core.Abstractions.Persistence;

public interface ISessionFolderRepository
{
    Task<IReadOnlyList<SessionFolder>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default);

    Task AddAsync(SessionFolder folder, CancellationToken cancellationToken = default);

    Task<int> DeleteTreeAsync(string path, CancellationToken cancellationToken = default);
}
