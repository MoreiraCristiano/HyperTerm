using Microsoft.EntityFrameworkCore;
using HyperTerm.Core.Abstractions.Persistence;
using Microsoft.Extensions.Logging;

namespace HyperTerm.Infrastructure.Persistence;

internal sealed class DatabaseInitializer(
    IDbContextFactory<HyperTermDbContext> contextFactory,
    ILogger<DatabaseInitializer> logger) : IDatabaseInitializer
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Database initialization started.");
        try
        {
            await using HyperTermDbContext context =
                await contextFactory.CreateDbContextAsync(cancellationToken);
            await context.Database.MigrateAsync(cancellationToken);
            logger.LogInformation("Database initialization completed.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Database initialization failed.");
            throw;
        }
    }
}
