namespace HyperTerm.Core.Services;

internal static class SessionFolderPath
{
    public static string Normalize(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string[] segments = path
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (segments.Length == 0 || segments.Any(segment => segment is "." or ".."))
        {
            throw new ArgumentException("Enter a valid folder path.", nameof(path));
        }

        string normalized = string.Join('/', segments);
        if (normalized.Length > 500)
        {
            throw new ArgumentException("Folder path cannot exceed 500 characters.", nameof(path));
        }

        return normalized;
    }

    public static string NormalizeOptional(string? path) =>
        string.IsNullOrWhiteSpace(path) ? string.Empty : Normalize(path);

    public static IEnumerable<string> ExpandAncestors(string path)
    {
        if (path.Length == 0)
        {
            yield break;
        }

        string[] segments = path.Split('/');
        for (int length = 1; length <= segments.Length; length++)
        {
            yield return string.Join('/', segments.Take(length));
        }
    }
}
