namespace SuperTerminal.Core.Models;

public sealed record ApplicationSettings
{
    public string PowerShellPath { get; init; } = "pwsh.exe";

    public string Theme { get; init; } = "Dark";

    public string TerminalFontFamily { get; init; } = "Cascadia Mono";

    public double TerminalFontSize { get; init; } = 13;

    public WindowSettings Window { get; init; } = new();
}

public sealed record WindowSettings
{
    public double Width { get; init; } = 1200;

    public double Height { get; init; } = 760;

    public int? X { get; init; }

    public int? Y { get; init; }
}
