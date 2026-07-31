namespace SuperTerminal.Core.Abstractions.Terminal;

public interface IPtySession : IAsyncDisposable
{
    event EventHandler<string>? OutputReceived;

    event EventHandler<int>? Exited;

    Task WriteAsync(string data, CancellationToken cancellationToken = default);

    void Resize(int columns, int rows);

    void Kill();
}
