using HyperTerm.UI.ViewModels;

namespace HyperTerm.UI.Controls;

internal sealed class HostedTerminal(
    TerminalTabViewModel tab,
    TerminalPaneViewModel pane)
{
    public HostedTerminal(TerminalTabViewModel tab)
        : this(tab, tab.ActivePane ?? throw new ArgumentException("Tab has no pane.", nameof(tab)))
    {
    }

    public TerminalTabViewModel Tab { get; } = tab;

    public TerminalPaneViewModel Pane { get; } = pane;

    public TerminalOutputBuffer Output { get; } = new();

    public SemaphoreSlim CreationGate { get; } = new(1, 1);

    public bool Created { get; set; }

    public bool Removed { get; set; }

    public bool WriteInFlight { get; set; }

    public long WriteToken { get; set; }

    public CancellationTokenSource? WriteTimeoutCancellation { get; set; }
}
