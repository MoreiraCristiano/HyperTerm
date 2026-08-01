namespace HyperTerm.UI.Services;

public interface ISessionArchiveFilePicker
{
    Task<Stream?> OpenExportStreamAsync(
        CancellationToken cancellationToken = default);

    Task<Stream?> OpenImportStreamAsync(
        CancellationToken cancellationToken = default);
}
