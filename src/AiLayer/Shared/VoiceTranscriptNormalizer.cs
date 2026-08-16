#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Fistix.TaskManager.AiLayer.Shared;

/// <summary>
/// Applies lightweight deterministic corrections for common STT slips in todo voice commands.
/// Prefer phrase-level fixes; avoid aggressive weekday fuzzy matching that mangles domain words
/// (e.g. "summary" → "sunday").
/// </summary>
public static class VoiceTranscriptNormalizer
{
    private static readonly string[] Weekdays =
    [
        "monday", "tuesday", "wednesday", "thursday", "friday", "saturday", "sunday"
    ];

    /// <summary>
    /// Domain / command words that must never be rewritten to weekdays via fuzzy match.
    /// </summary>
    private static readonly HashSet<string> ProtectedTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "summary", "summarize", "summarise", "priority", "regenerate", "generate",
        "read", "edit", "save", "cancel", "open", "close", "complete", "search",
        "due", "date", "task", "todo", "title", "description", "status",
        "semantic", "apply", "suggested", "mark", "set", "create", "update",
        "high", "medium", "low", "pending", "progress", "completed"
    };

    private static readonly (string Pattern, string Replacement)[] PhraseFixes =
    [
        // regenerate / priority
        (@"\bread\s+(?:the\s+)?priority\b", "regenerate the priority"),
        (@"\bre\s*generate\s+(?:the\s+)?priority\b", "regenerate the priority"),
        (@"\bregenerate\s+(?:the\s+)?priority\b", "regenerate the priority"),

        // regenerate / summary (STT often hears "sunday")
        (@"\bregenerate\s+(?:the\s+)?sunday\b", "regenerate the summary"),
        (@"\bread\s+(?:the\s+)?summary\b", "regenerate the summary"),
        (@"\bread\s+(?:the\s+)?sunday\b", "regenerate the summary"),
        (@"\bthe\s+sunday\s+of\s+this\s+task\b", "the summary of this task"),
        (@"\bsunday\s+of\s+this\s+task\b", "summary of this task"),
        (@"\bsunday\s+of\s+(?:the\s+)?task\b", "summary of the task"),

        // due date slips: "sunday last friday" → "due date last friday"
        (@"\b(?:set\s+)?(?:due\s+)?sunday\s+last\s+(monday|tuesday|wednesday|thursday|friday|saturday|sunday)\b",
            "set due date last $1"),
        (@"\b(?:set\s+)?(?:due\s+)?saturday\s+last\s+(monday|tuesday|wednesday|thursday|friday|saturday|sunday)\b",
            "set due date last $1"),
        (@"\blast\s+due\s+date\b", "set due date"),

        // weekday spelling slips (only known misspellings, not fuzzy)
        (@"\bsuch\s+a\s+day\b", "saturday"),
        (@"\bsatarday\b", "saturday"),
        (@"\bsaterday\b", "saturday"),
        (@"\bsundae\b", "sunday")
    ];

    public static string Normalize(string transcript, string? contextHint = null)
    {
        if (string.IsNullOrWhiteSpace(transcript))
        {
            return string.Empty;
        }

        var normalized = transcript.Trim();
        var context = contextHint?.ToLowerInvariant() ?? string.Empty;
        var editOpen = context.Contains("editopen=true", StringComparison.Ordinal)
                       || context.Contains("edit", StringComparison.Ordinal);

        // Context-aware edit slip: "added" → "edit" only when edit modal is open.
        if (editOpen)
        {
            normalized = Regex.Replace(
                normalized,
                @"\badd(?:ed)?\s+it\b",
                "edit it",
                RegexOptions.IgnoreCase);
            normalized = Regex.Replace(
                normalized,
                @"\badded\b",
                "edit",
                RegexOptions.IgnoreCase);
        }

        foreach (var (pattern, replacement) in PhraseFixes)
        {
            normalized = Regex.Replace(
                normalized,
                pattern,
                replacement,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        // Only fuzzy-correct clear misspellings near a weekday when a scheduling cue is present.
        if (HasSchedulingCue(normalized))
        {
            normalized = NormalizeNearWeekdays(normalized);
        }

        return CollapseWhitespace(normalized);
    }

    private static bool HasSchedulingCue(string value) =>
        Regex.IsMatch(
            value,
            @"\b(?:due|on|by|until|next|coming|last|tomorrow|schedule|appointment)\b",
            RegexOptions.IgnoreCase);

    private static string NormalizeNearWeekdays(string value)
    {
        var tokens = Regex.Matches(value, @"\b[a-zA-Z]{4,12}\b");
        if (tokens.Count == 0)
        {
            return value;
        }

        var replacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match tokenMatch in tokens)
        {
            var token = tokenMatch.Value;
            var lower = token.ToLowerInvariant();
            if (Array.Exists(Weekdays, d => d == lower) || ProtectedTokens.Contains(lower))
            {
                continue;
            }

            // Never fuzzy-map words that look like command nouns.
            if (lower.Contains("summ", StringComparison.Ordinal) ||
                lower.Contains("prior", StringComparison.Ordinal) ||
                lower.Contains("gener", StringComparison.Ordinal))
            {
                continue;
            }

            string? best = null;
            var bestDistance = int.MaxValue;
            foreach (var day in Weekdays)
            {
                var distance = Levenshtein(lower, day);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = day;
                }
            }

            // Strict: only 1–2 edits, and token must be close in length to a weekday.
            if (best is null || bestDistance is < 1 or > 2)
            {
                continue;
            }

            if (Math.Abs(lower.Length - best.Length) > 2)
            {
                continue;
            }

            replacements[token] = MatchCase(token, best);
        }

        foreach (var pair in replacements)
        {
            value = Regex.Replace(
                value,
                $@"\b{Regex.Escape(pair.Key)}\b",
                pair.Value,
                RegexOptions.IgnoreCase);
        }

        return value;
    }

    private static string MatchCase(string source, string replacement)
    {
        if (source.Length == 0)
        {
            return replacement;
        }

        if (char.IsUpper(source[0]))
        {
            return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(replacement);
        }

        return replacement;
    }

    private static string CollapseWhitespace(string value) =>
        Regex.Replace(value.Trim(), @"\s+", " ");

    private static int Levenshtein(string a, string b)
    {
        var dp = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) dp[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) dp[0, j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                dp[i, j] = Math.Min(
                    Math.Min(dp[i - 1, j] + 1, dp[i, j - 1] + 1),
                    dp[i - 1, j - 1] + cost);
            }
        }

        return dp[a.Length, b.Length];
    }
}
