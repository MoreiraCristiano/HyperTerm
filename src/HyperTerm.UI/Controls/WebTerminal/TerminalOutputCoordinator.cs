namespace HyperTerm.UI.Controls;

internal sealed class TerminalOutputCoordinator(
    Func<HostedTerminal[]> getTerminals,
    Func<HostedTerminal?> getActiveTerminal,
    Func<HostedTerminal, string, long, Task> writeOutput,
    Action scheduleFlush,
    Func<TimeSpan, CancellationToken, Task>? delay = null)
{
    private static readonly TimeSpan WriteTimeout = TimeSpan.FromSeconds(5);
    private readonly Func<TimeSpan, CancellationToken, Task> delay =
        delay ?? Task.Delay;

    public bool Enqueue(HostedTerminal hosted, string output)
    {
        if (!hosted.Output.Enqueue(output))
        {
            return false;
        }

        scheduleFlush();
        return true;
    }

    public void Complete(HostedTerminal hosted)
    {
        hosted.Output.Complete();
        hosted.WriteTimeoutCancellation?.Cancel();
        hosted.WriteTimeoutCancellation?.Dispose();
        hosted.WriteTimeoutCancellation = null;
    }

    public void Acknowledge(HostedTerminal hosted, long token, bool success)
    {
        if (!hosted.WriteInFlight || hosted.WriteToken != token)
        {
            return;
        }

        hosted.WriteTimeoutCancellation?.Cancel();
        hosted.WriteTimeoutCancellation?.Dispose();
        hosted.WriteTimeoutCancellation = null;
        hosted.WriteInFlight = false;
        if (!success)
        {
            hosted.Tab.ReportLaunchFailed("Terminal renderer rejected output.");
        }

        scheduleFlush();
    }

    public async Task<bool> FlushAsync()
    {
        int writesStarted = 0;
        HostedTerminal? activeHosted = getActiveTerminal();
        if (activeHosted is not null && await TryStartWriteAsync(activeHosted))
        {
            writesStarted++;
        }

        foreach (HostedTerminal hosted in getTerminals())
        {
            if (writesStarted >= 4)
            {
                break;
            }

            if (ReferenceEquals(hosted, activeHosted) ||
                !await TryStartWriteAsync(hosted))
            {
                continue;
            }

            writesStarted++;
        }

        return getTerminals().Any(hosted =>
            hosted.Created && !hosted.WriteInFlight && hosted.Output.HasData);
    }

    private async Task<bool> TryStartWriteAsync(HostedTerminal hosted)
    {
        if (!hosted.Created || hosted.WriteInFlight)
        {
            return false;
        }

        string? output = hosted.Output.TryDrainBatch();
        if (output is null)
        {
            return false;
        }

        long token = ++hosted.WriteToken;
        hosted.WriteInFlight = true;
        try
        {
            await writeOutput(hosted, output, token);
            hosted.WriteTimeoutCancellation?.Dispose();
            hosted.WriteTimeoutCancellation = new CancellationTokenSource();
            _ = ObserveWriteTimeoutAsync(
                hosted,
                token,
                hosted.WriteTimeoutCancellation.Token);
            return true;
        }
        catch (Exception exception)
        {
            hosted.WriteInFlight = false;
            hosted.Tab.ReportLaunchFailed(exception.Message);
            return false;
        }
    }

    private async Task ObserveWriteTimeoutAsync(
        HostedTerminal hosted,
        long token,
        CancellationToken cancellationToken)
    {
        try
        {
            await delay(WriteTimeout, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        if (!hosted.Removed && hosted.WriteInFlight && hosted.WriteToken == token)
        {
            hosted.WriteInFlight = false;
            hosted.Tab.ReportLaunchFailed("Terminal renderer stopped acknowledging output.");
            scheduleFlush();
        }
    }
}
