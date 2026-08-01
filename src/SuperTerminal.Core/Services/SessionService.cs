using SuperTerminal.Core.Abstractions.Persistence;
using SuperTerminal.Core.Abstractions.Services;
using SuperTerminal.Core.Entities;
using SuperTerminal.Core.Models;

namespace SuperTerminal.Core.Services;

internal sealed class SessionService(ISessionRepository repository) : ISessionService
{
    public Task<IReadOnlyList<Session>> GetAllAsync(
        CancellationToken cancellationToken = default) =>
        repository.GetAllAsync(cancellationToken);

    public Task<Session?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        EnsureValidId(id);
        return repository.GetByIdAsync(id, cancellationToken);
    }

    public async Task<Session> CreateAsync(
        SessionDetails details,
        CancellationToken cancellationToken = default)
    {
        SessionValidator.Validate(details);

        DateTime now = DateTime.UtcNow;
        var session = new Session(
            Guid.NewGuid(),
            details.Name.Trim(),
            details.Host.Trim(),
            details.Port,
            details.Username.Trim(),
            NormalizeOptional(details.PrivateKey),
            NormalizeFolder(details.Folder),
            NormalizeOptional(details.Notes),
            now,
            now);

        await repository.AddAsync(session, cancellationToken);
        return session;
    }

    public async Task<Session> UpdateAsync(
        Guid id,
        SessionDetails details,
        CancellationToken cancellationToken = default)
    {
        EnsureValidId(id);
        SessionValidator.Validate(details);

        Session session = await repository.GetByIdAsync(id, cancellationToken)
            ?? throw new KeyNotFoundException($"Session '{id}' was not found.");

        session.Update(
            details.Name.Trim(),
            details.Host.Trim(),
            details.Port,
            details.Username.Trim(),
            NormalizeOptional(details.PrivateKey),
            NormalizeFolder(details.Folder),
            NormalizeOptional(details.Notes),
            DateTime.UtcNow);

        await repository.UpdateAsync(session, cancellationToken);
        return session;
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        EnsureValidId(id);

        bool deleted = await repository.DeleteAsync(id, cancellationToken);
        if (!deleted)
        {
            throw new KeyNotFoundException($"Session '{id}' was not found.");
        }
    }

    public async Task<Session> MoveAsync(
        Guid id,
        string folder,
        CancellationToken cancellationToken = default)
    {
        EnsureValidId(id);
        Session session = await repository.GetByIdAsync(id, cancellationToken)
            ?? throw new KeyNotFoundException($"Session '{id}' was not found.");

        session.Update(
            session.Name,
            session.Host,
            session.Port,
            session.Username,
            session.PrivateKey,
            NormalizeFolder(folder),
            session.Notes,
            DateTime.UtcNow);

        await repository.UpdateAsync(session, cancellationToken);
        return session;
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string NormalizeFolder(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

    private static void EnsureValidId(Guid id)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Session ID cannot be empty.", nameof(id));
        }
    }
}
