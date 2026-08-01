using Microsoft.Extensions.DependencyInjection;
using HyperTerm.Core.Abstractions.Services;
using HyperTerm.Core.Services;

namespace HyperTerm.Core;

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
