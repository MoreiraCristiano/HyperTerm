using Microsoft.EntityFrameworkCore;
using SuperTerminal.Core.Abstractions.Persistence;

namespace SuperTerminal.Infrastructure.Persistence;

internal sealed class DatabaseInitializer(
    IDbContextFactory<SuperTerminalDbContext> contextFactory) : IDatabaseInitializer
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using SuperTerminalDbContext context =
            await contextFactory.CreateDbContextAsync(cancellationToken);

        await context.Database.MigrateAsync(cancellationToken);
    }
}
