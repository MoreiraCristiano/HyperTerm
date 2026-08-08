using System.Text;
using System.Threading.Channels;
using HyperTerm.Core.Abstractions.Terminal;
using HyperTerm.Infrastructure.Terminal;
using Microsoft.Extensions.Logging.Abstractions;

namespace HyperTerm.Infrastructure.Tests;

public sealed class PortaPtySessionUnitTests
{
    [Fact]
    public async Task Output_reader_preserves_utf8_characters_split_across_reads()
    {
        var stream = new ControlledReadStream();
        var output = new StringBuilder();
        Task readTask = Utf8PtyOutputReader.ReadAsync(
            stream,
            chunk => output.Append(chunk),
            TestContext.Current.CancellationToken);
        await stream.WaitForReadCallsAsync(1);
        byte[] bytes = Encoding.UTF8.GetBytes("Olá 👋 漢字");

        for (int index = 0; index < bytes.Length; index++)
        {
            stream.Enqueue([bytes[index]]);
            await stream.WaitForReadCallsAsync(index + 2);
        }

        stream.Complete();
        await readTask.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal("Olá 👋 漢字", output.ToString());
    }

    [Fact]
    public async Task Output_before_first_subscriber_is_replayed_in_order()
    {
        var reader = new ControlledReadStream();
        var connection = new FakePtyConnectionAdapter(reader);
        await using var session = CreateSession(connection);
        await reader.WaitForReadCallsAsync(1);

        reader.Enqueue(Encoding.UTF8.GetBytes("first"));
        await reader.WaitForReadCallsAsync(2);
        reader.Enqueue(Encoding.UTF8.GetBytes("-second"));
        await reader.WaitForReadCallsAsync(3);

        var output = new StringBuilder();
        session.OutputReceived += (_, chunk) => output.Append(chunk);

        Assert.Equal("first-second", output.ToString());
    }

    [Fact]
    public async Task Exit_is_signaled_once_and_replayed_to_a_late_subscriber()
    {
        var connection = new FakePtyConnectionAdapter(new ControlledReadStream());
        await using var session = CreateSession(connection);
        int firstCalls = 0;
        session.Exited += (_, _) => firstCalls++;

        connection.RaiseExit(17);
        connection.RaiseExit(99);
        int completion = await session.Completion.WaitAsync(TimeSpan.FromSeconds(2));
        int lateExitCode = 0;
        session.Exited += (_, code) => lateExitCode = code;

        Assert.Equal(17, completion);
        Assert.Equal(17, lateExitCode);
        Assert.Equal(1, firstCalls);
        Assert.Equal(TerminalSessionState.Exited, session.State);
    }

    [Fact]
    public async Task Throwing_subscriber_does_not_prevent_other_subscribers()
    {
        var reader = new ControlledReadStream();
        var connection = new FakePtyConnectionAdapter(reader);
        await using var session = CreateSession(connection);
        await reader.WaitForReadCallsAsync(1);
        var outputReceived = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var exitReceived = new TaskCompletionSource<int>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        session.OutputReceived += (_, _) => throw new InvalidOperationException("test");
        session.OutputReceived += (_, output) => outputReceived.TrySetResult(output);
        session.Exited += (_, _) => throw new InvalidOperationException("test");
        session.Exited += (_, code) => exitReceived.TrySetResult(code);

        reader.Enqueue(Encoding.UTF8.GetBytes("healthy"));
        connection.RaiseExit(4);

        Assert.Equal("healthy", await outputReceived.Task.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal(4, await exitReceived.Task.WaitAsync(TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public async Task Dispose_waits_for_active_write_and_is_idempotent()
    {
        var writer = new BlockingWriteStream();
        var connection = new FakePtyConnectionAdapter(
            new ControlledReadStream(),
            writer);
        var session = CreateSession(connection);

        Task writeTask = session.WriteAsync("input", TestContext.Current.CancellationToken);
        await writer.Started.WaitAsync(TimeSpan.FromSeconds(2));
        Task firstDispose = session.DisposeAsync().AsTask();
        Task secondDispose = session.DisposeAsync().AsTask();

        Assert.False(firstDispose.IsCompleted);
        Assert.False(secondDispose.IsCompleted);
        writer.Release();
        await Task.WhenAll(writeTask, firstDispose, secondDispose)
            .WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(1, connection.DisposeCalls);
        Assert.Equal(1, connection.KillCalls);
        Assert.Equal(TerminalSessionState.Disposed, session.State);
    }

    [Fact]
    public async Task Resize_failure_faults_session_and_ignores_later_operations()
    {
        var connection = new FakePtyConnectionAdapter(new ControlledReadStream())
        {
            ResizeException = new IOException("closed"),
        };
        await using var session = CreateSession(connection);

        session.Resize(0, 0);
        int exitCode = await session.Completion.WaitAsync(TimeSpan.FromSeconds(2));
        await session.WriteAsync("ignored", TestContext.Current.CancellationToken);
        session.Kill();

        Assert.Equal(-1, exitCode);
        Assert.Equal(TerminalSessionState.Faulted, session.State);
        Assert.Equal(1, connection.ResizeCalls);
        Assert.Equal((1, 1), connection.LastSize);
        Assert.Equal(1, connection.KillCalls);
        Assert.Equal(0, connection.Writer.Length);
    }

    private static PortaPtySession CreateSession(IPtyConnectionAdapter connection) =>
        new(connection, NullLogger.Instance);

    private sealed class FakePtyConnectionAdapter : IPtyConnectionAdapter
    {
        public FakePtyConnectionAdapter(Stream reader, Stream? writer = null)
        {
            ReaderStream = reader;
            WriterStream = writer ?? new MemoryStream();
        }

        public event EventHandler<int>? Exited;

        public Stream ReaderStream { get; }

        public Stream WriterStream { get; }

        public MemoryStream Writer => Assert.IsType<MemoryStream>(WriterStream);

        public Exception? ResizeException { get; init; }

        public int ResizeCalls { get; private set; }

        public int KillCalls { get; private set; }

        public int DisposeCalls { get; private set; }

        public (int Columns, int Rows) LastSize { get; private set; }

        public void Resize(int columns, int rows)
        {
            ResizeCalls++;
            LastSize = (columns, rows);
            if (ResizeException is not null)
            {
                throw ResizeException;
            }
        }

        public void Kill() => KillCalls++;

        public void Dispose()
        {
            DisposeCalls++;
            ReaderStream.Dispose();
            WriterStream.Dispose();
        }

        public void RaiseExit(int exitCode) => Exited?.Invoke(this, exitCode);
    }

    private sealed class ControlledReadStream : Stream
    {
        private readonly Channel<byte[]> chunks = Channel.CreateUnbounded<byte[]>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
        private readonly object callGate = new();
        private readonly List<TaskCompletionSource> callWaiters = [];
        private int readCalls;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public void Enqueue(byte[] bytes) => chunks.Writer.TryWrite(bytes);

        public void Complete() => chunks.Writer.TryComplete();

        public Task WaitForReadCallsAsync(int expectedCalls)
        {
            lock (callGate)
            {
                if (readCalls >= expectedCalls)
                {
                    return Task.CompletedTask;
                }

                while (callWaiters.Count < expectedCalls)
                {
                    callWaiters.Add(new TaskCompletionSource(
                        TaskCreationOptions.RunContinuationsAsynchronously));
                }

                return callWaiters[expectedCalls - 1].Task.WaitAsync(TimeSpan.FromSeconds(2));
            }
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            SignalReadCall();
            while (await chunks.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                if (!chunks.Reader.TryRead(out byte[]? chunk))
                {
                    continue;
                }

                Assert.True(chunk.Length <= buffer.Length);
                chunk.CopyTo(buffer);
                return chunk.Length;
            }

            return 0;
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Complete();
            }

            base.Dispose(disposing);
        }

        private void SignalReadCall()
        {
            TaskCompletionSource? waiter = null;
            lock (callGate)
            {
                readCalls++;
                if (callWaiters.Count >= readCalls)
                {
                    waiter = callWaiters[readCalls - 1];
                }
            }

            waiter?.TrySetResult();
        }
    }

    private sealed class BlockingWriteStream : MemoryStream
    {
        private readonly TaskCompletionSource started = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource release = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Started => started.Task;

        public void Release() => release.TrySetResult();

        public override async ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            started.TrySetResult();
            await release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            await base.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
        }
    }
}
