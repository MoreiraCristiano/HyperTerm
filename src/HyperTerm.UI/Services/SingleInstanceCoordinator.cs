using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;

namespace HyperTerm.UI.Services;

internal sealed class SingleInstanceCoordinator : IAsyncDisposable
{
    private const byte ActivateCommand = 1;
    private const byte AcknowledgedResponse = 1;
    private readonly object gate = new();
    private readonly Mutex instanceMutex;
    private readonly CancellationTokenSource listenerCancellation = new();
    private readonly string pipeName;
    private Action? activationHandler;
    private Task? listenerTask;
    private bool activationPending;
    private bool disposed;

    public SingleInstanceCoordinator(string instanceIdentity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceIdentity);

        string identityHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(instanceIdentity)))[..24];
        instanceMutex = new Mutex(
            initiallyOwned: false,
            $"Local\\HyperTerm.SingleInstance.{identityHash}",
            out bool createdNew);
        pipeName = $"HyperTerm.SingleInstance.{identityHash}";
        IsPrimary = createdNew;
    }

    public bool IsPrimary { get; }

    public static string CreateIdentity()
    {
        string identity = $"HyperTerm|Session:{Process.GetCurrentProcess().SessionId}";
        if (!string.Equals(
                Environment.GetEnvironmentVariable("HYPERTERM_TEST_MODE"),
                "1",
                StringComparison.Ordinal))
        {
            return identity;
        }

        string? testRoot = Environment.GetEnvironmentVariable("HYPERTERM_DATA_ROOT");
        string? normalizedTestRoot = string.IsNullOrWhiteSpace(testRoot)
            ? null
            : Path.GetFullPath(testRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .ToUpperInvariant();
        return string.IsNullOrWhiteSpace(testRoot)
            ? identity + "|Test"
            : identity + "|Test:" + normalizedTestRoot;
    }

    public void StartListening()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!IsPrimary)
            {
                throw new InvalidOperationException(
                    "Only the primary application instance can listen for activation.");
            }

            listenerTask ??= ListenAsync(listenerCancellation.Token);
        }
    }

    public void SetActivationHandler(Action handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        bool invokePending;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            activationHandler = handler;
            invokePending = activationPending;
            activationPending = false;
        }

        if (invokePending)
        {
            handler();
        }
    }

    public void ClearActivationHandler()
    {
        lock (gate)
        {
            activationHandler = null;
        }
    }

    public async Task<bool> RequestActivationAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (IsPrimary)
        {
            throw new InvalidOperationException(
                "The primary application instance cannot request its own activation.");
        }

        using var timeoutCancellation = new CancellationTokenSource(timeout);
        using var combinedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutCancellation.Token);
        try
        {
            await using var client = new NamedPipeClientStream(
                ".",
                pipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await client.ConnectAsync(combinedCancellation.Token).ConfigureAwait(false);

            byte[] processIdBuffer = new byte[sizeof(int)];
            await client.ReadExactlyAsync(
                processIdBuffer,
                combinedCancellation.Token).ConfigureAwait(false);
            int primaryProcessId = BitConverter.ToInt32(processIdBuffer);
            WindowsApplicationActivation.TryAllowForegroundActivation(primaryProcessId);

            await client.WriteAsync(
                new[] { ActivateCommand },
                combinedCancellation.Token).ConfigureAwait(false);
            await client.FlushAsync(combinedCancellation.Token).ConfigureAwait(false);

            byte[] response = new byte[1];
            await client.ReadExactlyAsync(
                response,
                combinedCancellation.Token).ConfigureAwait(false);
            return response[0] == AcknowledgedResponse;
        }
        catch (OperationCanceledException) when (
            timeoutCancellation.IsCancellationRequested &&
            !cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception exception) when (exception is IOException or TimeoutException)
        {
            return false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        Task? pendingListener;
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            activationHandler = null;
            listenerCancellation.Cancel();
            pendingListener = listenerTask;
        }

        if (pendingListener is not null)
        {
            try
            {
                await pendingListener.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (listenerCancellation.IsCancellationRequested)
            {
            }
        }

        listenerCancellation.Dispose();
        instanceMutex.Dispose();
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    pipeName,
                    PipeDirection.InOut,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);

                byte[] processId = BitConverter.GetBytes(Environment.ProcessId);
                await server.WriteAsync(processId, cancellationToken).ConfigureAwait(false);
                await server.FlushAsync(cancellationToken).ConfigureAwait(false);

                byte[] command = new byte[1];
                await server.ReadExactlyAsync(command, cancellationToken).ConfigureAwait(false);
                if (command[0] != ActivateCommand)
                {
                    continue;
                }

                QueueActivation();
                await server.WriteAsync(
                    new[] { AcknowledgedResponse },
                    cancellationToken).ConfigureAwait(false);
                await server.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (IOException) when (!cancellationToken.IsCancellationRequested)
            {
            }
        }
    }

    private void QueueActivation()
    {
        Action? handler;
        lock (gate)
        {
            handler = activationHandler;
            if (handler is null)
            {
                activationPending = true;
                return;
            }
        }

        handler();
    }
}
