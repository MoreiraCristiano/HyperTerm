using Avalonia;
using Avalonia.Styling;

namespace HyperTerm.UI.Services;

internal sealed class AvaloniaThemeService : IThemeService
{
    public void Apply(string theme)
    {
        if (Application.Current is null)
        {
            return;
        }

        Application.Current.RequestedThemeVariant = theme.Trim() switch
        {
            string value when value.Equals("Default Light", StringComparison.OrdinalIgnoreCase) =>
                ThemeVariant.Light,
            string value when value.Equals("Darcula", StringComparison.OrdinalIgnoreCase) =>
                ApplicationThemeVariants.Darcula,
            string value when value.Equals("Mintara", StringComparison.OrdinalIgnoreCase) =>
                ApplicationThemeVariants.Mintara,
            string value when value.Equals("Vesper", StringComparison.OrdinalIgnoreCase) =>
                ApplicationThemeVariants.Vesper,
            string value when value.Equals("Abyss", StringComparison.OrdinalIgnoreCase) =>
                ApplicationThemeVariants.Abyss,
            _ => ThemeVariant.Dark,
        };
    }
}
