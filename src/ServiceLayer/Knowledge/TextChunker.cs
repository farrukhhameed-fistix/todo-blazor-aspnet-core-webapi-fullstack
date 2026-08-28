#nullable enable

using System;
using System.Collections.Generic;

namespace Fistix.TaskManager.ServiceLayer.Knowledge;

public sealed record TextChunk(int Ordinal, string Content, string? Heading);

/// <summary>Fixed-size splitter with overlap; prefers whitespace near the cut.</summary>
public static class TextChunker
{
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

    private static string? ExtractHeading(string content)
    {
        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.StartsWith('#') && line.Length > 1)
            {
                var heading = line.TrimStart('#').Trim();
                return string.IsNullOrWhiteSpace(heading) ? null : heading;
            }
        }

        return null;
    }
}
