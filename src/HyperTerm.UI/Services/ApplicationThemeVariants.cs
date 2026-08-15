using Avalonia.Styling;

namespace HyperTerm.UI.Services;

public static class ApplicationThemeVariants
{
    public static ThemeVariant Darcula { get; } = new("Darcula", ThemeVariant.Dark);

    public static ThemeVariant Mintara { get; } = new("Mintara", ThemeVariant.Dark);

    public static ThemeVariant Vesper { get; } = new("Vesper", ThemeVariant.Dark);

    public static ThemeVariant Abyss { get; } = new("Abyss", ThemeVariant.Dark);

    public static ThemeVariant Aurora { get; } = new("Aurora", ThemeVariant.Light);

    public static ThemeVariant MintaraLight { get; } = new("Mintara Light", ThemeVariant.Light);

    public static ThemeVariant VesperLight { get; } = new("Vesper Light", ThemeVariant.Light);

    public static ThemeVariant AbyssLight { get; } = new("Abyss Light", ThemeVariant.Light);
}
