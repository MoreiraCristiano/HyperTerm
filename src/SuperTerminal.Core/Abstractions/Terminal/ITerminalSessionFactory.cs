using SuperTerminal.Core.Entities;
using SuperTerminal.Core.Models;

namespace SuperTerminal.Core.Abstractions.Terminal;

public interface ITerminalSessionFactory
{
    Task<TerminalSessionDefinition> CreateLocalAsync(
        CancellationToken cancellationToken = default);

    Task<TerminalSessionDefinition> CreateAsync(
        Session session,
        CancellationToken cancellationToken = default);
}
