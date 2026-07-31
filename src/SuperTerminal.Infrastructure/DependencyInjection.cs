using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SuperTerminal.Core.Abstractions.Persistence;
using SuperTerminal.Core.Abstractions.Settings;
using SuperTerminal.Core.Abstractions.Terminal;
using SuperTerminal.Infrastructure.Persistence;
using SuperTerminal.Infrastructure.Persistence.Repositories;
using SuperTerminal.Infrastructure.Settings;
using SuperTerminal.Infrastructure.Storage;
using SuperTerminal.Infrastructure.Terminal;

namespace SuperTerminal.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IApplicationPathProvider, ApplicationPathProvider>();
        services.AddDbContextFactory<SuperTerminalDbContext>((serviceProvider, options) =>
        {
            IApplicationPathProvider pathProvider =
                serviceProvider.GetRequiredService<IApplicationPathProvider>();

            options.UseSqlite($"Data Source={pathProvider.DatabasePath}");
        });
        services.AddSingleton<IDatabaseInitializer, DatabaseInitializer>();
        services.AddSingleton<ISessionRepository, SessionRepository>();
        services.AddSingleton<ISessionFolderRepository, SessionFolderRepository>();
        services.AddSingleton<ISettingsService, JsonSettingsService>();
        services.AddSingleton<ITerminalSessionFactory, PowerShellSessionFactory>();
        services.AddSingleton<IPtySessionFactory, PortaPtySessionFactory>();

        return services;
    }
}
