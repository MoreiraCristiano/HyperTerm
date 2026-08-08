using System.Text;
using Porta.Pty;
using HyperTerm.Core.Abstractions.Terminal;
using HyperTerm.Core.Models;
using Microsoft.Extensions.Logging;

namespace HyperTerm.Infrastructure.Terminal;

internal sealed class PortaPtySessionFactory(
    ILogger<PortaPtySessionFactory> logger) : IPtySessionFactory
{
    public async Task<IPtySession> CreateAsync(
        TerminalSessionDefinition definition,
        int columns,
        int rows,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        logger.LogInformation("Starting a PTY process.");

        var options = new PtyOptions
        {
            Name = definition.Process,
            App = definition.Process,
            CommandLine = definition.Arguments.ToArray(),
            Cwd = definition.StartingDirectory,
            Cols = Math.Max(1, columns),
            Rows = Math.Max(1, rows),
        };

        IPtyConnection connection = await PtyProvider.SpawnAsync(options, cancellationToken);
        logger.LogInformation("PTY process started.");
        return new PortaPtySession(connection, logger);
    }

    private sealed class PortaPtySession : IPtySession
    {
        private static readonly Encoding Utf8 = new UTF8Encoding(false);
        private const int MaximumPendingOutputCharacters = 2 * 1024 * 1024;
        private readonly IPtyConnection connection;
        private readonly object eventGate = new();
        private readonly CancellationTokenSource lifetime = new();
        private readonly SemaphoreSlim writeGate = new(1, 1);
        private readonly Queue<string> pendingOutput = new();
        private readonly TaskCompletionSource<int> completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource disposalCompletion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Task readTask;
        private int disposeStarted;
        private int exitSignaled;
        private int lastExitCode = -1;
        private int pendingOutputCharacters;
        private int state = (int)TerminalSessionState.Running;
        private EventHandler<string>? outputReceived;
        private EventHandler<int>? exited;

        private readonly ILogger logger;

        public PortaPtySession(IPtyConnection connection, ILogger logger)
        {
            this.connection = connection;
            this.logger = logger;
            connection.ProcessExited += OnProcessExited;
            readTask = Task.Run(() => ReadOutputAsync(lifetime.Token));
        }

        public event EventHandler<string>? OutputReceived
        {
            add
            {
                ArgumentNullException.ThrowIfNull(value);
                string[] pending = [];
                lock (eventGate)
                {
                    bool firstSubscriber = outputReceived is null;
                    outputReceived += value;
                    if (firstSubscriber && pendingOutput.Count > 0)
                    {
                        pending = pendingOutput.ToArray();
                        pendingOutput.Clear();
                        pendingOutputCharacters = 0;
                        Monitor.PulseAll(eventGate);
                    }
                }

                foreach (string output in pending)
                {
                    InvokeOutputHandler(value, output);
                }
            }
            remove
            {
                lock (eventGate)
                {
                    outputReceived -= value;
                }
            }
        }

        public event EventHandler<int>? Exited
        {
            add
            {
                ArgumentNullException.ThrowIfNull(value);
                int? completedExitCode = null;
                lock (eventGate)
                {
                    exited += value;
                    if (exitSignaled != 0)
                    {
                        completedExitCode = lastExitCode;
                    }
                }

                if (completedExitCode is int exitCode)
                {
                    InvokeExitHandler(value, exitCode);
                }
            }
            remove
            {
                lock (eventGate)
                {
                    exited -= value;
                }
            }
        }

        public TerminalSessionState State =>
            (TerminalSessionState)Volatile.Read(ref state);

        public Task<int> Completion => completion.Task;

        public async Task WriteAsync(
            string data,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(data) || State != TerminalSessionState.Running)
            {
                return;
            }

            await writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (State != TerminalSessionState.Running)
                {
                    return;
                }

                byte[] bytes = Utf8.GetBytes(data);
                await connection.WriterStream
                    .WriteAsync(bytes, cancellationToken)
                    .ConfigureAwait(false);
                await connection.WriterStream
                    .FlushAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is IOException or ObjectDisposedException or InvalidOperationException)
            {
                HandleTerminalFailure(exception, "PTY input writer failed.");
            }
            finally
            {
                writeGate.Release();
            }
        }

        public void Resize(int columns, int rows)
        {
            if (State == TerminalSessionState.Running)
            {
                try
                {
                    connection.Resize(Math.Max(1, columns), Math.Max(1, rows));
                }
                catch (Exception exception) when (
                    exception is IOException or ObjectDisposedException or InvalidOperationException)
                {
                    HandleTerminalFailure(exception, "PTY resize failed.");
                }
            }
        }

        public void Kill()
        {
            if (State == TerminalSessionState.Running)
            {
                try
                {
                    connection.Kill();
                }
                catch (Exception exception) when (
                    exception is IOException or ObjectDisposedException or InvalidOperationException)
                {
                    HandleTerminalFailure(exception, "PTY termination failed.");
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref disposeStarted, 1) != 0)
            {
                await disposalCompletion.Task.ConfigureAwait(false);
                return;
            }

            try
            {
                Volatile.Write(ref state, (int)TerminalSessionState.Disposing);
                lock (eventGate)
                {
                    Monitor.PulseAll(eventGate);
                }
                connection.ProcessExited -= OnProcessExited;
                lifetime.Cancel();

                await writeGate.WaitAsync().ConfigureAwait(false);
                try
                {
                    try
                    {
                        connection.Kill();
                    }
                    catch (Exception exception) when (
                        exception is IOException or ObjectDisposedException or InvalidOperationException)
                    {
                        logger.LogDebug(exception, "PTY was already stopped during disposal.");
                    }

                    try
                    {
                        connection.Dispose();
                    }
                    catch (Exception exception) when (
                        exception is IOException or ObjectDisposedException or InvalidOperationException)
                    {
                        logger.LogDebug(exception, "PTY connection was already disposed.");
                    }
                }
                finally
                {
                    writeGate.Release();
                }

                try
                {
                    await readTask
                        .WaitAsync(TimeSpan.FromSeconds(2))
                        .ConfigureAwait(false);
                }
                catch (Exception exception) when (
                    exception is OperationCanceledException or
                        ObjectDisposedException or IOException or TimeoutException)
                {
                }
            }
            finally
            {
                lifetime.Dispose();
                Volatile.Write(ref state, (int)TerminalSessionState.Disposed);
                completion.TrySetResult(-1);
                disposalCompletion.TrySetResult();
            }
        }

        private async Task ReadOutputAsync(CancellationToken cancellationToken)
        {
            try
            {
                byte[] buffer = new byte[64 * 1024];
                char[] characters = new char[Utf8.GetMaxCharCount(buffer.Length)];
                Decoder decoder = Utf8.GetDecoder();
                while (!cancellationToken.IsCancellationRequested)
                {
                    int count = await connection.ReaderStream.ReadAsync(
                        buffer,
                        cancellationToken);
                    if (count == 0)
                    {
                        int remaining = decoder.GetChars(
                            ReadOnlySpan<byte>.Empty,
                            characters,
                            flush: true);
                        if (remaining > 0)
                        {
                            RaiseOutput(new string(characters, 0, remaining));
                        }

                        return;
                    }

                    int characterCount = decoder.GetChars(
                        buffer.AsSpan(0, count),
                        characters,
                        flush: false);
                    if (characterCount > 0)
                    {
                        RaiseOutput(new string(characters, 0, characterCount));
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception exception) when (
                exception is IOException or ObjectDisposedException)
            {
                if (State == TerminalSessionState.Running)
                {
                    HandleTerminalFailure(exception, "PTY output reader failed.");
                }
                else
                {
                    logger.LogDebug(exception, "PTY output reader stopped during shutdown.");
                }
            }
        }

        private void OnProcessExited(object? sender, PtyExitedEventArgs eventArgs)
        {
            int exitCode = eventArgs.ExitCode;
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("PTY process exited with code {ExitCode}.", exitCode);
            }
            SignalExit(exitCode, TerminalSessionState.Exited);
        }

        private void HandleTerminalFailure(Exception exception, string message)
        {
            if (State != TerminalSessionState.Running)
            {
                return;
            }

            logger.LogWarning(exception, "PTY operation failed: {Operation}", message);
            SignalExit(-1, TerminalSessionState.Faulted);
            try
            {
                connection.Kill();
            }
            catch (Exception killException) when (
                killException is IOException or ObjectDisposedException or InvalidOperationException)
            {
                logger.LogDebug(killException, "PTY was already stopped after failure.");
            }
        }

        private void SignalExit(int exitCode, TerminalSessionState terminalState)
        {
            EventHandler<int>[] handlers;
            lock (eventGate)
            {
                if (exitSignaled != 0)
                {
                    return;
                }

                exitSignaled = 1;
                lastExitCode = exitCode;
                Volatile.Write(ref state, (int)terminalState);
                handlers = exited?.GetInvocationList().Cast<EventHandler<int>>().ToArray() ?? [];
                Monitor.PulseAll(eventGate);
            }

            completion.TrySetResult(exitCode);
            foreach (EventHandler<int> handler in handlers)
            {
                InvokeExitHandler(handler, exitCode);
            }
        }

        private void RaiseOutput(string output)
        {
            EventHandler<string>[] handlers;
            lock (eventGate)
            {
                while (outputReceived is null &&
                       pendingOutputCharacters >= MaximumPendingOutputCharacters &&
                       State is not (
                           TerminalSessionState.Disposing or TerminalSessionState.Disposed))
                {
                    Monitor.Wait(eventGate);
                }

                if (outputReceived is null)
                {
                    if (State is TerminalSessionState.Disposing or TerminalSessionState.Disposed)
                    {
                        return;
                    }

                    pendingOutput.Enqueue(output);
                    pendingOutputCharacters += output.Length;
                    return;
                }

                handlers = outputReceived
                    .GetInvocationList()
                    .Cast<EventHandler<string>>()
                    .ToArray();
            }

            foreach (EventHandler<string> handler in handlers)
            {
                InvokeOutputHandler(handler, output);
            }
        }

        private void InvokeOutputHandler(EventHandler<string> handler, string output)
        {
            try
            {
                handler(this, output);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "PTY output subscriber failed.");
            }
        }

        private void InvokeExitHandler(EventHandler<int> handler, int exitCode)
        {
            try
            {
                handler(this, exitCode);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "PTY exit subscriber failed.");
            }
        }
    }
}
