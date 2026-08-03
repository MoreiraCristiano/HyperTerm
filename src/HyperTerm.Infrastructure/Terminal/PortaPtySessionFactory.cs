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
        private readonly IPtyConnection connection;
        private readonly CancellationTokenSource lifetime = new();
        private readonly SemaphoreSlim writeGate = new(1, 1);
        private readonly Task readTask;
        private int disposed;

        private readonly ILogger logger;

        public PortaPtySession(IPtyConnection connection, ILogger logger)
        {
            this.connection = connection;
            this.logger = logger;
            connection.ProcessExited += OnProcessExited;
            readTask = Task.Run(() => ReadOutputAsync(lifetime.Token));
        }

        public event EventHandler<string>? OutputReceived;

        public event EventHandler<int>? Exited;

        public async Task WriteAsync(
            string data,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(data) || Volatile.Read(ref disposed) != 0)
            {
                return;
            }

            await writeGate.WaitAsync(cancellationToken);
            try
            {
                byte[] bytes = Utf8.GetBytes(data);
                await connection.WriterStream.WriteAsync(bytes, cancellationToken);
                await connection.WriterStream.FlushAsync(cancellationToken);
            }
            finally
            {
                writeGate.Release();
            }
        }

        public void Resize(int columns, int rows)
        {
            if (Volatile.Read(ref disposed) == 0)
            {
                connection.Resize(Math.Max(1, columns), Math.Max(1, rows));
            }
        }

        public void Kill()
        {
            if (Volatile.Read(ref disposed) == 0)
            {
                connection.Kill();
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
            {
                return;
            }

            connection.ProcessExited -= OnProcessExited;
            lifetime.Cancel();
            try
            {
                connection.Kill();
            }
            catch
            {
            }

            connection.Dispose();

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

            writeGate.Dispose();
            lifetime.Dispose();
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
                            OutputReceived?.Invoke(
                                this,
                                new string(characters, 0, remaining));
                        }

                        return;
                    }

                    int characterCount = decoder.GetChars(
                        buffer.AsSpan(0, count),
                        characters,
                        flush: false);
                    if (characterCount > 0)
                    {
                        OutputReceived?.Invoke(
                            this,
                            new string(characters, 0, characterCount));
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception exception) when (
                exception is IOException or ObjectDisposedException)
            {
                logger.LogError(exception, "PTY output reader failed.");
            }
        }

        private void OnProcessExited(object? sender, PtyExitedEventArgs eventArgs)
        {
            logger.LogInformation("PTY process exited with code {ExitCode}.", eventArgs.ExitCode);
            Exited?.Invoke(this, eventArgs.ExitCode);
        }
    }
}
