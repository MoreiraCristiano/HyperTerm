using Porta.Pty;

namespace HyperTerm.Infrastructure.Terminal;

internal sealed class PortaPtyConnectionAdapter : IPtyConnectionAdapter
{
    private readonly IPtyConnection connection;
    private readonly object eventGate = new();
    private EventHandler<int>? exited;
    private int exitSignaled;
    private int lastExitCode = -1;

    public PortaPtyConnectionAdapter(IPtyConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        this.connection = connection;
        connection.ProcessExited += OnProcessExited;
        if (connection.WaitForExit(0))
        {
            SignalExit(connection.ExitCode);
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
                value(this, exitCode);
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

    public Stream ReaderStream => connection.ReaderStream;

    public Stream WriterStream => connection.WriterStream;

    public void Resize(int columns, int rows) => connection.Resize(columns, rows);

    public void Kill() => connection.Kill();

    public void Dispose()
    {
        connection.ProcessExited -= OnProcessExited;
        connection.Dispose();
    }

    private void OnProcessExited(object? sender, PtyExitedEventArgs eventArgs) =>
        SignalExit(eventArgs.ExitCode);

    private void SignalExit(int exitCode)
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
            handlers = exited?.GetInvocationList().Cast<EventHandler<int>>().ToArray() ?? [];
        }

        foreach (EventHandler<int> handler in handlers)
        {
            handler(this, exitCode);
        }
    }
}
