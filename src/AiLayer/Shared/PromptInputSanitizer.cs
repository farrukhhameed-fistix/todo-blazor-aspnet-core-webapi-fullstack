using System.Text;

namespace Fistix.TaskManager.AiLayer.Shared;

public static class PromptInputSanitizer
{
    public static string SanitizeAndTruncate(string? input, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var sanitized = Sanitize(input);
        return sanitized.Length <= maxLength ? sanitized : sanitized[..maxLength];
    }

    public static string Sanitize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var withoutControlChars = StripControlCharacters(input);

        return withoutControlChars
            .Replace("<!--", string.Empty, StringComparison.Ordinal)
            .Replace("-->", string.Empty, StringComparison.Ordinal)
            .Replace("{", "{{", StringComparison.Ordinal)
            .Replace("}", "}}", StringComparison.Ordinal);
    }

    /// <summary>
    /// Strips control characters for display/output text without escaping braces
    /// (brace escaping is only needed for Semantic Kernel template inputs).
    /// </summary>
    public static string StripControlCharacters(string? input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(input.Length);

        foreach (var character in input)
        {
            if (character is '\n' or '\r' or '\t' || !char.IsControl(character))
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }
}
