namespace HyperTerm.Core.Models;

public sealed record ApplicationSettings
{
    public string PowerShellPath { get; init; } = "pwsh.exe";

    public IReadOnlyList<TerminalProfile> TerminalProfiles { get; init; } = [];

    public string DefaultTerminalProfileId { get; init; } = TerminalProfileIds.PowerShell;

    public string Theme { get; init; } = "Default Dark";

    public string TerminalFontFamily { get; init; } = "Cascadia Mono";

    public double TerminalFontSize { get; init; } = 13;

    public string TerminalSelectionColor { get; init; } = "#264F78";

    public string TerminalCursorStyle { get; init; } = "Bar";

    public bool TerminalCursorBlink { get; init; } = true;

    public bool ShowSidebarScrollbar { get; init; }

    public bool CloseToSystemTray { get; init; }

    public bool CaptureLogs { get; init; } = true;

    public bool KeepPsmuxSessionsOnExit { get; init; } = true;

    public WindowSettings Window { get; init; } = new();
}

public sealed record WindowSettings
{
    public double Width { get; init; } = 1200;

    public double Height { get; init; } = 760;

    public int? X { get; init; }

    public int? Y { get; init; }
}
