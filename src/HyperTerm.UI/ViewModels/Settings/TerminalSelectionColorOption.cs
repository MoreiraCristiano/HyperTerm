using Avalonia.Media;

namespace HyperTerm.UI.ViewModels;

public sealed record TerminalSelectionColorOption
{
    public TerminalSelectionColorOption(string name, string value, string? previewColor = null)
    {
        Name = name;
        Value = value;
        PreviewBrush = new SolidColorBrush(Color.Parse(previewColor ?? value));
    }

    public string Name { get; }

    public string Value { get; }

    public IBrush PreviewBrush { get; }
}
