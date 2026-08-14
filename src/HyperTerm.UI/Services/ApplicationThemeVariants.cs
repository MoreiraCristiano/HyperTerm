using Avalonia.Styling;

namespace HyperTerm.UI.Services;

public static class ApplicationThemeVariants
{
    public static ThemeVariant Darcula { get; } = new("Darcula", ThemeVariant.Dark);

    public static ThemeVariant Mintara { get; } = new("Mintara", ThemeVariant.Dark);

    public static ThemeVariant Vesper { get; } = new("Vesper", ThemeVariant.Dark);

    public static ThemeVariant Abyss { get; } = new("Abyss", ThemeVariant.Dark);
}
