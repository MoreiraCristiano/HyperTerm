namespace HyperTerm.UI.Services;

public interface IExecutableFilePicker
{
    Task<string?> PickExecutableAsync(
        string title,
        CancellationToken cancellationToken = default);
}
