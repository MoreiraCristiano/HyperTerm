using HyperTerm.Core.Entities;
using HyperTerm.Core.Models;

namespace HyperTerm.Core.Abstractions.Terminal;

public interface ITerminalSessionFactory
{
    Task<TerminalSessionDefinition> CreateLocalAsync(
        CancellationToken cancellationToken = default);

    Task<TerminalSessionDefinition> CreateAsync(
        Session session,
        CancellationToken cancellationToken = default);
}
