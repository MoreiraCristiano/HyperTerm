using Avalonia;
using Avalonia.Styling;

namespace SuperTerminal.UI.Services;

internal sealed class AvaloniaThemeService : IThemeService
{
    public void Apply(string theme)
    {
        if (Application.Current is null)
        {
            return;
        }

        Application.Current.RequestedThemeVariant = ThemeVariant.Dark;
    }
}
