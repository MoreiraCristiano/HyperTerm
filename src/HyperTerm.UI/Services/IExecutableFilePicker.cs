namespace HyperTerm.UI.Services;

public interface IExecutableFilePicker
{
    Task<string?> PickPowerShellAsync(CancellationToken cancellationToken = default);
}
