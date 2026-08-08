using System.Diagnostics;
using HyperTerm.Core.Exceptions;

namespace HyperTerm.Infrastructure.Terminal;

internal interface IPsmuxCommandClient
{
    string? TryResolveExecutable();

    Task<PsmuxCommandResult> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}

internal sealed class PsmuxCommandClient : IPsmuxCommandClient
{
    private static readonly string BundledExecutablePath =
        Path.Combine("tools", "psmux", "psmux.exe");

    public string? TryResolveExecutable() =>
        WindowsExecutableResolver.TryResolveBundledOrPath(
            BundledExecutablePath,
            "psmux.exe",
            "psmux.exe");

    public async Task<PsmuxCommandResult> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = PsmuxService.GetDefaultStartingDirectory(),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new TerminalLaunchException($"Could not start ‘{executable}’.");
        }

        Task<string> outputTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        Task<string> errorTask = process.StandardError.ReadToEndAsync(CancellationToken.None);
        using var timeoutCancellation = new CancellationTokenSource(timeout);
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutCancellation.Token);
        try
        {
            await process.WaitForExitAsync(linkedCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            TryTerminate(process);
            await DrainOutputAsync(outputTask, errorTask);
            cancellationToken.ThrowIfCancellationRequested();
            throw new TerminalLaunchException(
                $"psmux did not respond within {timeout.TotalSeconds:0} seconds.");
        }

        return new PsmuxCommandResult(
            process.ExitCode,
            await outputTask,
            await errorTask);
    }

    private static void TryTerminate(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }
    }

    private static async Task DrainOutputAsync(params Task<string>[] outputTasks)
    {
        try
        {
            await Task.WhenAll(outputTasks);
        }
        catch (Exception exception) when (
            exception is IOException or InvalidOperationException or ObjectDisposedException)
        {
        }
    }
}

internal sealed record PsmuxCommandResult(
    int ExitCode,
    string StandardOutput,
    string StandardError);
