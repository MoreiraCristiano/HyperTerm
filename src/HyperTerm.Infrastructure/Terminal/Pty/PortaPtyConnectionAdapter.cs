using Porta.Pty;

namespace HyperTerm.Infrastructure.Terminal;

internal sealed class PortaPtyConnectionAdapter : IPtyConnectionAdapter
{
    private readonly IPtyConnection connection;

    public PortaPtyConnectionAdapter(IPtyConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        this.connection = connection;
        connection.ProcessExited += OnProcessExited;
    }

    public event EventHandler<int>? Exited;

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
        Exited?.Invoke(this, eventArgs.ExitCode);
}
