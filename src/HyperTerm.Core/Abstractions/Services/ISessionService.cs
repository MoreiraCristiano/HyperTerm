using HyperTerm.Core.Entities;
using HyperTerm.Core.Models;

namespace HyperTerm.Core.Abstractions.Services;

public interface ISessionService
{
    Task<IReadOnlyList<Session>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<Session?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Session> CreateAsync(
        SessionDetails details,
        CancellationToken cancellationToken = default);

    Task<Session> UpdateAsync(
        Guid id,
        SessionDetails details,
        CancellationToken cancellationToken = default);

    Task<Session> MoveAsync(
        Guid id,
        string folder,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
