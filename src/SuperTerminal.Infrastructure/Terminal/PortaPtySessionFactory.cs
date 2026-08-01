using System.Text;
using Porta.Pty;
using SuperTerminal.Core.Abstractions.Terminal;
using SuperTerminal.Core.Models;

namespace SuperTerminal.Infrastructure.Terminal;

internal sealed class PortaPtySessionFactory : IPtySessionFactory
{
    public async Task<IPtySession> CreateAsync(
        TerminalSessionDefinition definition,
        int columns,
        int rows,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);

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
        return new PortaPtySession(connection);
    }

    private sealed class PortaPtySession : IPtySession
    {
        private static readonly Encoding Utf8 = new UTF8Encoding(false);
        private readonly IPtyConnection connection;
        private readonly CancellationTokenSource lifetime = new();
        private readonly SemaphoreSlim writeGate = new(1, 1);
        private readonly Task readTask;
        private int disposed;

        public PortaPtySession(IPtyConnection connection)
        {
            this.connection = connection;
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
            byte[] buffer = new byte[64 * 1024];
            char[] characters = new char[Utf8.GetMaxCharCount(buffer.Length)];
            Decoder decoder = Utf8.GetDecoder();
            while (!cancellationToken.IsCancellationRequested)
            {
                int count = await connection.ReaderStream.ReadAsync(buffer, cancellationToken);
                if (count == 0)
                {
                    int remaining = decoder.GetChars(
                        ReadOnlySpan<byte>.Empty,
                        characters,
                        flush: true);
                    if (remaining > 0)
                    {
                        OutputReceived?.Invoke(this, new string(characters, 0, remaining));
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

        private void OnProcessExited(object? sender, PtyExitedEventArgs eventArgs) =>
            Exited?.Invoke(this, eventArgs.ExitCode);
    }
}
