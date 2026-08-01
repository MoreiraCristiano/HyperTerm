using HyperTerm.Core.Models;

namespace HyperTerm.Core.Abstractions.Terminal;

public interface IPtySessionFactory
{
    Task<IPtySession> CreateAsync(
        TerminalSessionDefinition definition,
        int columns,
        int rows,
        CancellationToken cancellationToken = default);
}
