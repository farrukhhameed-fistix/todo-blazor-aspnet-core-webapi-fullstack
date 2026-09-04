#nullable enable

using System;
using System.Collections.Generic;

namespace Fistix.TaskManager.ServiceLayer.Knowledge;

public sealed record TextChunk(int Ordinal, string Content, string? Heading);

/// <summary>
/// Fixed-size splitter with overlap; prefers markdown headings, table row ends, then whitespace.
/// </summary>
public static class TextChunker
{
    /// <summary>Matches <c>KnowledgeChunk.Heading</c> EF max length.</summary>
    public const int MaxHeadingLength = 500;

    public static IReadOnlyList<TextChunk> Split(string? text, int chunkSize, int chunkOverlap)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<TextChunk>();
        }

        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        if (normalized.Length == 0)
        {
            return Array.Empty<TextChunk>();
        }

        chunkSize = Math.Max(32, chunkSize);
        chunkOverlap = Math.Clamp(chunkOverlap, 0, Math.Max(0, chunkSize - 1));

        var chunks = new List<TextChunk>();
        var lastHeading = (string?)null;
        var start = 0;
        var ordinal = 0;

        while (start < normalized.Length)
        {
            var remaining = normalized.Length - start;
            var take = Math.Min(chunkSize, remaining);
            var end = start + take;

            if (end < normalized.Length)
            {
                var window = normalized.AsSpan(start, take);
                var breakAt = FindBreak(window);
                if (breakAt > chunkSize / 4)
                {
                    end = start + breakAt;
                }
            }

            var content = normalized[start..end].Trim();
            if (content.Length > 0)
            {
                lastHeading = ExtractHeading(content) ?? lastHeading;
                chunks.Add(new TextChunk(ordinal, content, lastHeading));
                ordinal++;
            }

            if (end >= normalized.Length)
            {
                break;
            }

            var nextStart = end - chunkOverlap;
            if (nextStart <= start)
            {
                nextStart = end;
            }

            start = nextStart;
        }

        return chunks;
    }

    private static int FindBreak(ReadOnlySpan<char> window)
    {
        // Prefer a markdown heading start near the end of the window (next chunk keeps the heading).
        for (var i = window.Length - 1; i >= window.Length / 4; i--)
        {
            if (window[i] == '\n' && i + 1 < window.Length && window[i + 1] == '#')
            {
                return i + 1;
            }
        }

        // Prefer end of a markdown table row.
        for (var i = window.Length - 1; i >= window.Length / 4; i--)
        {
            if (window[i] == '\n' && LooksLikeTableRowEnd(window, i))
            {
                return i + 1;
            }
        }

        for (var i = window.Length - 1; i >= 0; i--)
        {
            if (window[i] == '\n')
            {
                return i + 1;
            }
        }

        for (var i = window.Length - 1; i >= 0; i--)
        {
            if (char.IsWhiteSpace(window[i]))
            {
                return i + 1;
            }
        }

        return window.Length;
    }

    private static bool LooksLikeTableRowEnd(ReadOnlySpan<char> window, int newlineIndex)
    {
        var lineStart = newlineIndex;
        while (lineStart > 0 && window[lineStart - 1] != '\n')
        {
            lineStart--;
        }

        var line = window[lineStart..newlineIndex].Trim();
        return line.Length > 0 && line[0] == '|' && line[^1] == '|';
    }

    private static string? ExtractHeading(string content)
    {
        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.Trim();
            if (!TryParseMarkdownHeading(line, out var heading))
            {
                continue;
            }

            return TruncateHeading(heading);
        }

        return null;
    }

    /// <summary>Accepts <c># Title</c> … <c>###### Title</c>; rejects bare <c>#</c> or hash-tags without space.</summary>
    private static bool TryParseMarkdownHeading(string line, out string heading)
    {
        heading = string.Empty;
        if (line.Length < 3 || line[0] != '#')
        {
            return false;
        }

        var i = 0;
        while (i < line.Length && i < 6 && line[i] == '#')
        {
            i++;
        }

        if (i == 0 || i >= line.Length || !char.IsWhiteSpace(line[i]))
        {
            return false;
        }

        heading = line[i..].Trim();
        return !string.IsNullOrWhiteSpace(heading);
    }

    private static string TruncateHeading(string heading) =>
        heading.Length <= MaxHeadingLength ? heading : heading[..MaxHeadingLength];
}
