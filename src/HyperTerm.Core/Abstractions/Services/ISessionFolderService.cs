using HyperTerm.Core.Entities;
using HyperTerm.Core.Models;

namespace HyperTerm.Core.Abstractions.Services;

public interface ISessionFolderService
{
    Task<IReadOnlyList<SessionFolder>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<SessionFolder> CreateAsync(string path, CancellationToken cancellationToken = default);

    Task RenameAsync(
        string currentPath,
        string newPath,
        CancellationToken cancellationToken = default);

    Task<FolderDeleteResult> DeleteAsync(
        IReadOnlyCollection<string> paths,
        bool force,
        CancellationToken cancellationToken = default);
}
