using System.Text.RegularExpressions;

namespace EventAlertManagement.Infrastructure.Security;

/// <summary>Last line of defence before an error message is stored or shown: no URLs (they may hold webhook tokens), bounded length.</summary>
public static partial class ErrorSanitizer
{
    [GeneratedRegex(@"[a-zA-Z][a-zA-Z0-9+.\-]*://\S+")]
    private static partial Regex UrlPattern();

    public static string Sanitize(string? message, int maxLength = 300)
    {
        if (string.IsNullOrWhiteSpace(message)) return "Unknown error";
        var cleaned = UrlPattern().Replace(message, "[url removed]");
        cleaned = cleaned.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return cleaned.Length <= maxLength ? cleaned : cleaned[..maxLength] + "…";
    }
}
