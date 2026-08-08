using HyperTerm.Core;
using HyperTerm.Core.Abstractions.Persistence;
using HyperTerm.Core.Abstractions.Services;
using HyperTerm.Core.Abstractions.Settings;
using HyperTerm.Core.Abstractions.Terminal;
using HyperTerm.Infrastructure.Persistence;
using HyperTerm.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace HyperTerm.Infrastructure.Tests;

[Collection("Environment")]
public sealed class DependencyInjectionTests
{
    [Fact]
    public void Complete_core_and_infrastructure_graph_resolves()
    {
        string directory = Path.Combine(Path.GetTempPath(), "HyperTerm.Tests", Guid.NewGuid().ToString("N"));
        using var testMode = new EnvironmentScope("HYPERTERM_TEST_MODE", "1");
        using var dataRoot = new EnvironmentScope("HYPERTERM_DATA_ROOT", directory);
        try
        {
            var services = new ServiceCollection();
            services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
            services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
            services.AddCore();
            services.AddInfrastructure();
            using ServiceProvider provider = services.BuildServiceProvider(
                new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

            Assert.NotNull(provider.GetRequiredService<ISessionService>());
            Assert.NotNull(provider.GetRequiredService<ISessionFolderService>());
            Assert.NotNull(provider.GetRequiredService<ISessionArchiveService>());
            Assert.NotNull(provider.GetRequiredService<ISettingsService>());
            Assert.NotNull(provider.GetRequiredService<ITerminalSessionFactory>());
            Assert.NotNull(provider.GetRequiredService<IPtySessionFactory>());
            Assert.NotNull(provider.GetRequiredService<IPsmuxService>());
            Assert.NotNull(provider.GetRequiredService<IDbContextFactory<HyperTermDbContext>>());
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Database_initializer_applies_migrations()
    {
        await using var database = await TemporaryDatabase.CreateUninitializedAsync();
        var initializer = new DatabaseInitializer(
            database.Factory,
            NullLogger<DatabaseInitializer>.Instance);

        await initializer.InitializeAsync(TestContext.Current.CancellationToken);

        await using HyperTermDbContext context = await database.Factory.CreateDbContextAsync();
        Assert.Equal(2, (await context.Database.GetAppliedMigrationsAsync()).Count());
    }

    [Fact]
    public async Task Database_initializer_preserves_initialization_failure()
    {
        var expected = new IOException("database unavailable");
        var initializer = new DatabaseInitializer(
            new FailingDbContextFactory(expected),
            NullLogger<DatabaseInitializer>.Instance);

        IOException actual = await Assert.ThrowsAsync<IOException>(() =>
            initializer.InitializeAsync(TestContext.Current.CancellationToken));

        Assert.Same(expected, actual);
    }
}

internal sealed class FailingDbContextFactory(Exception exception)
    : IDbContextFactory<HyperTermDbContext>
{
    public HyperTermDbContext CreateDbContext() => throw exception;

    public Task<HyperTermDbContext> CreateDbContextAsync(
        CancellationToken cancellationToken = default) => throw exception;
}
