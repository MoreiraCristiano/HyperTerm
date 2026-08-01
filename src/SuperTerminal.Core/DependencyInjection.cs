using Microsoft.Extensions.DependencyInjection;
using SuperTerminal.Core.Abstractions.Services;
using SuperTerminal.Core.Services;

namespace SuperTerminal.Core;

public static class DependencyInjection
{
    public static IServiceCollection AddCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<ISessionService, SessionService>();
        services.AddSingleton<ISessionFolderService, SessionFolderService>();
        services.AddSingleton<ISessionArchiveService, SessionArchiveService>();
        return services;
    }
}
