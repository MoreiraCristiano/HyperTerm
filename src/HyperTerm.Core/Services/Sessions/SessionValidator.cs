using HyperTerm.Core.Models;

namespace HyperTerm.Core.Services;

internal static class SessionValidator
{
    public static void Validate(SessionDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);

        ValidateRequired(details.Name, nameof(details.Name), 200);
        ValidateRequired(details.Host, nameof(details.Host), 253);
        ValidateRequired(details.Username, nameof(details.Username), 128);
        ValidateLength(details.PrivateKey, nameof(details.PrivateKey), 1024);
        ValidateLength(details.Folder, nameof(details.Folder), 500);
        ValidateLength(details.Notes, nameof(details.Notes), 4000);

        if (details.Port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(
                nameof(details),
                details.Port,
                "Details.Port must be between 1 and 65535.");
        }
    }

    private static void ValidateRequired(string value, string parameterName, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value is required.", parameterName);
        }

        ValidateLength(value.Trim(), parameterName, maximumLength);
    }

    private static void ValidateLength(string? value, string parameterName, int maximumLength)
    {
        if (value?.Trim().Length > maximumLength)
        {
            throw new ArgumentException(
                $"Value cannot exceed {maximumLength} characters.",
                parameterName);
        }
    }
}
