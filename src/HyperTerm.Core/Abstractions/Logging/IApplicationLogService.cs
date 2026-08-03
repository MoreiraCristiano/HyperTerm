namespace HyperTerm.Core.Abstractions.Logging;

public interface IApplicationLogService
{
    bool IsEnabled { get; }
    bool PreviousRunCrashed { get; }
    string LogsDirectory { get; }
    event EventHandler? LogChanged;
    void Configure(bool enabled);
    Task<string> ReadTailAsync(
        int maximumBytes = 512 * 1024,
        CancellationToken cancellationToken = default);
    void CompleteRun();
}
