using Avalonia.Media;

namespace SuperTerminal.UI.Services;

internal sealed class AvaloniaSystemFontService : ISystemFontService
{
    public IReadOnlyList<string> GetInstalledFontFamilies() =>
        FontManager.Current.SystemFonts
            .Select(font => font.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
}
