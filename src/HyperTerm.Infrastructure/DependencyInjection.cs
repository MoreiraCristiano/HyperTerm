using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using HyperTerm.Core.Abstractions.Persistence;
using HyperTerm.Core.Abstractions.Logging;
using HyperTerm.Core.Abstractions.Settings;
using HyperTerm.Core.Abstractions.Terminal;
using HyperTerm.Infrastructure.Persistence;
using HyperTerm.Infrastructure.Persistence.Repositories;
using HyperTerm.Infrastructure.Settings;
using HyperTerm.Infrastructure.Logging;
using HyperTerm.Infrastructure.Storage;
using HyperTerm.Infrastructure.Terminal;

namespace HyperTerm.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IApplicationPathProvider, ApplicationPathProvider>();
        services.AddSingleton<ApplicationLogService>();
        services.AddSingleton<IApplicationLogService>(provider =>
            provider.GetRequiredService<ApplicationLogService>());
        services.AddSingleton<Microsoft.Extensions.Logging.ILoggerProvider>(provider =>
            provider.GetRequiredService<ApplicationLogService>());
        services.AddDbContextFactory<HyperTermDbContext>((serviceProvider, options) =>
        {
            IApplicationPathProvider pathProvider =
                serviceProvider.GetRequiredService<IApplicationPathProvider>();

            options.UseSqlite($"Data Source={pathProvider.DatabasePath}");
        });
        services.AddSingleton<IDatabaseInitializer, DatabaseInitializer>();
        services.AddSingleton<ISessionRepository, SessionRepository>();
        services.AddSingleton<ISessionFolderRepository, SessionFolderRepository>();
        services.AddSingleton<ISessionImportRepository, SessionImportRepository>();
        services.AddSingleton<ISettingsService, JsonSettingsService>();
        services.AddSingleton<ITerminalProfileResolver, TerminalProfileResolver>();
        services.AddSingleton<ITerminalSessionFactory, TerminalSessionFactory>();
        services.AddSingleton<IPsmuxCommandClient, PsmuxCommandClient>();
        services.AddSingleton<IPsmuxService, PsmuxService>();
        services.AddSingleton<IPtySessionFactory, PortaPtySessionFactory>();

        return services;
    }
}
