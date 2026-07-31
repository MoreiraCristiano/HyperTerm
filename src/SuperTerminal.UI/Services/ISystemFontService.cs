namespace SuperTerminal.UI.Services;

public interface ISystemFontService
{
    IReadOnlyList<string> GetInstalledFontFamilies();
}
