using System.Text.RegularExpressions;

namespace HyperTerm.UI.ViewModels;

internal static partial class PsmuxSessionNameValidator
{
    public const string ErrorMessage =
        "Use 1–64 letters, numbers, underscores, or hyphens; start with a letter or number.";

    public static bool IsValid(string name) => NamePattern().IsMatch(name);

    [GeneratedRegex(
        "^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$",
        RegexOptions.CultureInvariant)]
    private static partial Regex NamePattern();
}
