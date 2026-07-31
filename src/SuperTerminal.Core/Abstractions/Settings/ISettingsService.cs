using SuperTerminal.Core.Models;

namespace SuperTerminal.Core.Abstractions.Settings;

public interface ISettingsService
{
    Task<ApplicationSettings> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(
        ApplicationSettings settings,
        CancellationToken cancellationToken = default);
}
