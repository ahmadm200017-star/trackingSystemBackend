using System.Text;

namespace MdfTracker.Api.Validation;

/// <summary>Cleans free-text the device supplies before it is stored or rendered.</summary>
public static class InputSanitizer
{
    /// <summary>
    /// Trims, collapses to null when empty, and strips control characters.
    ///
    /// Device model strings come from the handset and end up in the dashboard and in log
    /// lines. A NUL or an ANSI escape sequence in there is not a security hole here, but it
    /// does corrupt whatever renders it, and there is no legitimate reason for one.
    /// </summary>
    public static string? Clean(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            // Tab and newline are control characters too, but a single-line label has no
            // use for them either - fold them to a space rather than dropping the word join.
            if (char.IsControl(character))
            {
                if (character is '\t' or '\n' or '\r')
                {
                    builder.Append(' ');
                }
                continue;
            }
            builder.Append(character);
        }

        var cleaned = builder.ToString().Trim();
        while (cleaned.Contains("  ", StringComparison.Ordinal))
        {
            cleaned = cleaned.Replace("  ", " ", StringComparison.Ordinal);
        }

        if (cleaned.Length == 0)
        {
            return null;
        }

        return cleaned.Length > maxLength ? cleaned[..maxLength] : cleaned;
    }
}
