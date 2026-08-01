using HyperTerm.Core.Models;

namespace HyperTerm.Core.Abstractions.Settings;

public interface ISettingsService
{
    bool Exists();

    Task<ApplicationSettings> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(
        ApplicationSettings settings,
        CancellationToken cancellationToken = default);
}
