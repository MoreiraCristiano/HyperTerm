namespace HyperTerm.UI.Services;

public interface ILogInteractionService
{
    Task CopyAsync(string text, CancellationToken cancellationToken = default);
    Task OpenFolderAsync(string path, CancellationToken cancellationToken = default);
}
