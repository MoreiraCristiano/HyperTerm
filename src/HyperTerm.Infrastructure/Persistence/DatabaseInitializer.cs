using Microsoft.EntityFrameworkCore;
using HyperTerm.Core.Abstractions.Persistence;

namespace HyperTerm.Infrastructure.Persistence;

internal sealed class DatabaseInitializer(
    IDbContextFactory<HyperTermDbContext> contextFactory) : IDatabaseInitializer
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using HyperTermDbContext context =
            await contextFactory.CreateDbContextAsync(cancellationToken);

        await context.Database.MigrateAsync(cancellationToken);
    }
}
