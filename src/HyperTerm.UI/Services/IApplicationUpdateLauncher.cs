using HyperTerm.Core.Models;

namespace HyperTerm.UI.Services;

public interface IApplicationUpdateLauncher
{
    Task<bool> LaunchAsync(
        PreparedApplicationUpdate update,
        CancellationToken cancellationToken = default);
}
