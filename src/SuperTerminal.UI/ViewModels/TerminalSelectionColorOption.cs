using Avalonia.Media;

namespace SuperTerminal.UI.ViewModels;

public sealed record TerminalSelectionColorOption
{
    public TerminalSelectionColorOption(string name, string value)
    {
        Name = name;
        Value = value;
        PreviewBrush = new SolidColorBrush(Color.Parse(value));
    }

    public string Name { get; }

    public string Value { get; }

    public IBrush PreviewBrush { get; }
}
