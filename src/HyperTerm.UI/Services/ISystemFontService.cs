namespace HyperTerm.UI.Services;

public interface ISystemFontService
{
    IReadOnlyList<string> GetInstalledFontFamilies();
}
