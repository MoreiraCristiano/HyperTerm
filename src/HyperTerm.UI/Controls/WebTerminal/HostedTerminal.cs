using HyperTerm.UI.ViewModels;

namespace HyperTerm.UI.Controls;

internal sealed class HostedTerminal(TerminalTabViewModel tab)
{
    public TerminalTabViewModel Tab { get; } = tab;

    public TerminalOutputBuffer Output { get; } = new();

    public SemaphoreSlim CreationGate { get; } = new(1, 1);

    public bool Created { get; set; }

    public bool Removed { get; set; }

    public bool WriteInFlight { get; set; }

    public long WriteToken { get; set; }

    public CancellationTokenSource? WriteTimeoutCancellation { get; set; }
}
