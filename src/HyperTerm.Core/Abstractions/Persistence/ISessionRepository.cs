using HyperTerm.Core.Entities;

namespace HyperTerm.Core.Abstractions.Persistence;

public interface ISessionRepository
{
    Task<IReadOnlyList<Session>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<Session?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task AddAsync(Session session, CancellationToken cancellationToken = default);

    Task UpdateAsync(Session session, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
