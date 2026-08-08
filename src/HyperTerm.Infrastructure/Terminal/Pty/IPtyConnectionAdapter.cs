namespace HyperTerm.Infrastructure.Terminal;

internal interface IPtyConnectionAdapter : IDisposable
{
    event EventHandler<int>? Exited;

    Stream ReaderStream { get; }

    Stream WriterStream { get; }

    void Resize(int columns, int rows);

    void Kill();
}
