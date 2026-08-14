using Avalonia.Threading;
using HyperTerm.Core.Abstractions.Terminal;
using HyperTerm.Core.Models;

namespace HyperTerm.UI.ViewModels;

public sealed class TerminalPaneViewModel : IAsyncDisposable
{
    private readonly IPtySessionFactory ptySessionFactory;
    private readonly SemaphoreSlim startGate = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private IPtySession? ptySession;
    private int disposed;

    public TerminalPaneViewModel(
        Guid paneId,
        TerminalSessionDefinition definition,
        IPtySessionFactory ptySessionFactory)
    {
        PaneId = paneId;
        Definition = definition;
        this.ptySessionFactory = ptySessionFactory;
    }

    public Guid PaneId { get; }

    public TerminalSessionDefinition Definition { get; }

    public bool IsDisposed => Volatile.Read(ref disposed) != 0;

    public event EventHandler<string>? OutputReceived;

    public event EventHandler<int>? Exited;

    public event EventHandler? Started;

    public async Task StartAsync(
        int columns,
        int rows,
        CancellationToken cancellationToken = default)
    {
        if (IsDisposed)
        {
            return;
        }

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            lifetime.Token);
        await startGate.WaitAsync(linkedCancellation.Token);
        try
        {
            if (IsDisposed)
            {
                return;
            }

            if (ptySession is not null)
            {
                ptySession.Resize(columns, rows);
                return;
            }

            ptySession = await ptySessionFactory.CreateAsync(
                Definition,
                columns,
                rows,
                linkedCancellation.Token);
            ptySession.OutputReceived += OnOutputReceived;
            ptySession.Exited += OnExited;
            Started?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException) when (linkedCancellation.IsCancellationRequested)
        {
        }
        finally
        {
            startGate.Release();
        }
    }

    public Task WriteAsync(string data, CancellationToken cancellationToken = default) =>
        !IsDisposed && ptySession is not null
            ? ptySession.WriteAsync(data, cancellationToken)
            : Task.CompletedTask;

    public void Resize(int columns, int rows)
    {
        if (!IsDisposed)
        {
            ptySession?.Resize(columns, rows);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        lifetime.Cancel();
        await startGate.WaitAsync();
        try
        {
            if (ptySession is not null)
            {
                ptySession.OutputReceived -= OnOutputReceived;
                ptySession.Exited -= OnExited;
                await ptySession.DisposeAsync();
                ptySession = null;
            }
        }
        finally
        {
            startGate.Release();
            startGate.Dispose();
            lifetime.Dispose();
        }
    }

    private void OnOutputReceived(object? sender, string output)
    {
        if (!IsDisposed)
        {
            OutputReceived?.Invoke(this, output);
        }
    }

    private void OnExited(object? sender, int exitCode)
    {
        if (IsDisposed)
        {
            return;
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            Exited?.Invoke(this, exitCode);
        }
        else
        {
            Dispatcher.UIThread.Post(() => Exited?.Invoke(this, exitCode));
        }
    }
}

public sealed class TerminalPaneOutputEventArgs(Guid paneId, string output) : EventArgs
{
    public Guid PaneId { get; } = paneId;

    public string Output { get; } = output;
}
