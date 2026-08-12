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

        Application.Current.RequestedThemeVariant =
            theme.Equals("Default Light", StringComparison.OrdinalIgnoreCase)
                ? ThemeVariant.Light
                : ThemeVariant.Dark;
    }
}
