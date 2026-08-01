using HyperTerm.Core.Entities;
using HyperTerm.Core.Models;

namespace HyperTerm.Core.Abstractions.Persistence;

public interface ISessionFolderRepository
{
    Task<IReadOnlyList<SessionFolder>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default);

    Task AddAsync(SessionFolder folder, CancellationToken cancellationToken = default);

    Task<bool> RenameTreeAsync(
        string currentPath,
        string newPath,
        CancellationToken cancellationToken = default);

    Task<FolderDeleteResult> DeleteTreesAsync(
        IReadOnlyCollection<string> paths,
        bool force,
        CancellationToken cancellationToken = default);
}
