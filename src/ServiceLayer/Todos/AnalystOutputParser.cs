#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Fistix.TaskManager.AiLayer.Shared;

namespace Fistix.TaskManager.ServiceLayer.Todos;

public static partial class AnalystOutputParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static AnalystOutput Parse(string? analystText, IReadOnlyList<Guid> validCandidateIds, SprintWorkloadStats? stats = null)
    {
        var raw = analystText ?? string.Empty;
        if (TryParseJson(raw, out var fromJson))
        {
            fromJson.Stats ??= stats;
            fromJson.Summary = LlmOutputValidator.ValidateAgentText(fromJson.Summary);
            return FilterToValidIds(fromJson, validCandidateIds);
        }

        var sanitized = LlmOutputValidator.ValidateAgentText(raw);
        var ids = ExtractGuids(sanitized)
            .Where(validCandidateIds.Contains)
            .Distinct()
            .ToList();

        var risks = new List<string>();
        if (stats?.Overdue > 0)
        {
            risks.Add($"{stats.Overdue} overdue candidate(s).");
        }

        if (stats?.DueInSprintWindow == 0 && stats?.TotalCandidates > 0)
        {
            risks.Add("No candidates due within the sprint window.");
        }

        return new AnalystOutput
        {
            RecommendedIds = ids,
            Risks = risks,
            Theme = ExtractTheme(sanitized),
            Summary = sanitized,
            Stats = stats
        };
    }

    private static bool TryParseJson(string text, out AnalystOutput output)
    {
        output = new AnalystOutput();
        var json = ExtractJsonObject(text);
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("recommendedIds", out var idsEl) && idsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in idsEl.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String
                        && Guid.TryParse(item.GetString(), out var id))
                    {
                        output.RecommendedIds.Add(id);
                    }
                }
            }

            if (root.TryGetProperty("risks", out var risksEl) && risksEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in risksEl.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        var risk = item.GetString();
                        if (!string.IsNullOrWhiteSpace(risk))
                        {
                            output.Risks.Add(risk.Trim());
                        }
                    }
                }
            }

            if (root.TryGetProperty("theme", out var themeEl) && themeEl.ValueKind == JsonValueKind.String)
            {
                output.Theme = themeEl.GetString()?.Trim() ?? string.Empty;
            }

            if (root.TryGetProperty("summary", out var summaryEl) && summaryEl.ValueKind == JsonValueKind.String)
            {
                output.Summary = summaryEl.GetString()?.Trim() ?? text;
            }
            else
            {
                output.Summary = text;
            }

            return output.RecommendedIds.Count > 0 || output.Risks.Count > 0 || !string.IsNullOrWhiteSpace(output.Summary);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static AnalystOutput FilterToValidIds(AnalystOutput output, IReadOnlyList<Guid> validCandidateIds)
    {
        output.RecommendedIds = output.RecommendedIds
            .Where(validCandidateIds.Contains)
            .Distinct()
            .ToList();
        return output;
    }

    private static IEnumerable<Guid> ExtractGuids(string text)
    {
        foreach (Match match in GuidRegex().Matches(text))
        {
            if (Guid.TryParse(match.Value, out var id))
            {
                yield return id;
            }
        }
    }

    private static string ExtractTheme(string text)
    {
        var match = ThemeRegex().Match(text);
        return match.Success ? match.Groups[1].Value.Trim() : string.Empty;
    }

    private static string? ExtractJsonObject(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return null;
        }

        return text[start..(end + 1)];
    }

    [GeneratedRegex(@"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b")]
    private static partial Regex GuidRegex();

    [GeneratedRegex(@"(?i)theme\s*:\s*(.+)$", RegexOptions.Multiline)]
    private static partial Regex ThemeRegex();
}
