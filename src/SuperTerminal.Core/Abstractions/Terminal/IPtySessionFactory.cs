using SuperTerminal.Core.Models;

namespace SuperTerminal.Core.Abstractions.Terminal;

public interface IPtySessionFactory
{
    Task<IPtySession> CreateAsync(
        TerminalSessionDefinition definition,
        int columns,
        int rows,
        CancellationToken cancellationToken = default);
}
