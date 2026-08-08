using HyperTerm.Core.Abstractions.Terminal;
using HyperTerm.Core.Models;
using Microsoft.Extensions.Logging;
using Porta.Pty;

namespace HyperTerm.Infrastructure.Terminal;

internal sealed class PortaPtySessionFactory(
    ILogger<PortaPtySessionFactory> logger) : IPtySessionFactory
{
    public async Task<IPtySession> CreateAsync(
        TerminalSessionDefinition definition,
        int columns,
        int rows,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        cancellationToken.ThrowIfCancellationRequested();
        logger.LogInformation("Starting a PTY process.");

        var options = new PtyOptions
        {
            Name = definition.Process,
            App = definition.Process,
            CommandLine = definition.Arguments.ToArray(),
            Cwd = definition.StartingDirectory,
            Cols = Math.Max(1, columns),
            Rows = Math.Max(1, rows),
        };

        IPtyConnection connection = await PtyProvider.SpawnAsync(options, cancellationToken);
        logger.LogInformation("PTY process started.");
        return new PortaPtySession(new PortaPtyConnectionAdapter(connection), logger);
    }
}
