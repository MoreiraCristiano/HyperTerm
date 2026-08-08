using HyperTerm.Core.Models;

namespace HyperTerm.Core.Abstractions.Terminal;

public interface IPsmuxService
{
    Task<PsmuxAvailability> ProbeAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PsmuxSessionInfo>> ListSessionsAsync(
        CancellationToken cancellationToken = default);

    Task<TerminalSessionDefinition> CreateSessionDefinitionAsync(
        string name,
        CancellationToken cancellationToken = default);

    Task<TerminalSessionDefinition> CreateAttachDefinitionAsync(
        string name,
        CancellationToken cancellationToken = default);

    Task KillSessionAsync(string name, CancellationToken cancellationToken = default);

    Task<bool> TryStopServerAsync(CancellationToken cancellationToken = default);
}
