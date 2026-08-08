namespace HyperTerm.Core.Abstractions.Terminal;

public enum TerminalSessionState
{
    Starting,
    Running,
    Exited,
    Faulted,
    Disposing,
    Disposed,
}

public interface IPtySession : IAsyncDisposable
{
    event EventHandler<string>? OutputReceived;

    event EventHandler<int>? Exited;

    TerminalSessionState State { get; }

    Task<int> Completion { get; }

    Task WriteAsync(string data, CancellationToken cancellationToken = default);

    void Resize(int columns, int rows);

    void Kill();
}
