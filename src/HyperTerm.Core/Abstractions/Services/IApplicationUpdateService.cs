using HyperTerm.Core.Models;

namespace HyperTerm.Core.Abstractions.Services;

public interface IApplicationUpdateService
{
    Task<ApplicationUpdateInfo?> CheckAsync(
        Version currentVersion,
        CancellationToken cancellationToken = default);

    Task<PreparedApplicationUpdate> PrepareAsync(
        ApplicationUpdateInfo update,
        IProgress<ApplicationUpdateProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
