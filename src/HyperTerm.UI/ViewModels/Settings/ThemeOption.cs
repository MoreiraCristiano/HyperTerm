using Avalonia.Media;

namespace HyperTerm.UI.ViewModels;

public sealed record ThemeOption
{
    public ThemeOption(
        string name,
        string value,
        string description,
        string previewBackground,
        string previewHeader,
        string previewSidebar)
    {
        Name = name;
        Value = value;
        Description = description;
        PreviewBackground = new SolidColorBrush(Color.Parse(previewBackground));
        PreviewHeader = new SolidColorBrush(Color.Parse(previewHeader));
        PreviewSidebar = new SolidColorBrush(Color.Parse(previewSidebar));
    }

    public string Name { get; }
    public string Value { get; }
    public string Description { get; }
    public IBrush PreviewBackground { get; }
    public IBrush PreviewHeader { get; }
    public IBrush PreviewSidebar { get; }
}
