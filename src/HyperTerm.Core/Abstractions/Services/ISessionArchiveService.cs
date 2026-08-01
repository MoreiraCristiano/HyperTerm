using HyperTerm.Core.Models;

namespace HyperTerm.Core.Abstractions.Services;

public interface ISessionArchiveService
{
    Task ExportAsync(
        Stream destination,
        CancellationToken cancellationToken = default);

    Task<SessionImportResult> ImportAsync(
        Stream source,
        CancellationToken cancellationToken = default);
}
