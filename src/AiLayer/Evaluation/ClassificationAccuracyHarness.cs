#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Fistix.TaskManager.AiLayer.Shared;

namespace Fistix.TaskManager.AiLayer.Evaluation;

public sealed class ClassifyEvalRow
{
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string? ExpectedPriority { get; init; }
    public string? Category { get; init; }
}

public sealed class ClassifyEvalCaseResult
{
    public ClassifyEvalRow Row { get; init; } = new();
    public string PredictedPriority { get; init; } = string.Empty;
    public float Confidence { get; init; }
    public bool IsMatch { get; init; }
    public bool IsSafetyCase { get; init; }
}

public sealed class ClassifyAccuracyReport
{
    public int TotalLabeled { get; set; }
    public int Correct { get; set; }
    public double Accuracy => TotalLabeled == 0 ? 0 : (double)Correct / TotalLabeled;
    public string PromptVersion { get; set; } = AiPromptVersions.Classify;
    public string? Model { get; set; }
    public List<ClassifyEvalCaseResult> Cases { get; set; } = [];
    public Dictionary<string, Dictionary<string, int>> ConfusionMatrix { get; set; } = new();
}

/// <summary>
/// Offline classify accuracy harness. CI uses mocked predictions; live LLM via AI_EVAL_LIVE=1.
/// </summary>
public static class ClassificationAccuracyHarness
{
    public static bool IsLiveEvalEnabled =>
        string.Equals(Environment.GetEnvironmentVariable("AI_EVAL_LIVE"), "1", StringComparison.Ordinal);

    public static IReadOnlyList<ClassifyEvalRow> LoadCsv(string path)
    {
        var lines = File.ReadAllLines(path);
        if (lines.Length < 2)
        {
            return Array.Empty<ClassifyEvalRow>();
        }

        var header = ParseCsvLine(lines[0]).Select(h => h.Trim().ToLowerInvariant()).ToList();
        var titleIdx = header.IndexOf("title");
        var descIdx = header.IndexOf("description");
        var expectedIdx = header.IndexOf("expectedpriority");
        var categoryIdx = header.IndexOf("category");

        var rows = new List<ClassifyEvalRow>();
        for (var i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]))
            {
                continue;
            }

            var fields = ParseCsvLine(lines[i]);
            string Get(int idx) => idx >= 0 && idx < fields.Count ? fields[idx] : string.Empty;

            rows.Add(new ClassifyEvalRow
            {
                Title = Get(titleIdx),
                Description = Get(descIdx),
                ExpectedPriority = string.IsNullOrWhiteSpace(Get(expectedIdx)) ? null : Get(expectedIdx),
                Category = string.IsNullOrWhiteSpace(Get(categoryIdx)) ? null : Get(categoryIdx)
            });
        }

        return rows;
    }

    public static ClassifyAccuracyReport Score(
        IEnumerable<ClassifyEvalRow> rows,
        Func<ClassifyEvalRow, (string Priority, float Confidence)> predict,
        string? model = null)
    {
        var report = new ClassifyAccuracyReport
        {
            Model = model,
            PromptVersion = AiPromptVersions.Classify
        };

        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.ExpectedPriority))
            {
                continue;
            }

            var (priority, confidence) = predict(row);
            var expected = ClassificationGuardrails.NormalizePriority(row.ExpectedPriority);
            var predicted = ClassificationGuardrails.NormalizePriority(priority);
            var isMatch = string.Equals(expected, predicted, StringComparison.Ordinal);
            var isSafety = IsSafetyCase(row);

            report.TotalLabeled++;
            if (isMatch)
            {
                report.Correct++;
            }

            if (!report.ConfusionMatrix.TryGetValue(expected, out var rowMap))
            {
                rowMap = new Dictionary<string, int>(StringComparer.Ordinal);
                report.ConfusionMatrix[expected] = rowMap;
            }

            rowMap.TryGetValue(predicted, out var count);
            rowMap[predicted] = count + 1;

            report.Cases.Add(new ClassifyEvalCaseResult
            {
                Row = row,
                PredictedPriority = predicted,
                Confidence = confidence,
                IsMatch = isMatch,
                IsSafetyCase = isSafety
            });
        }

        return report;
    }

    /// <summary>
    /// Applies deterministic guardrails as a stand-in predict for CI when no LLM is available.
    /// </summary>
    public static (string Priority, float Confidence) PredictWithGuardrailsOnly(ClassifyEvalRow row)
    {
        var (priority, confidence, _) = ClassificationGuardrails.Apply(
            "MEDIUM",
            0.5f,
            reason: null,
            row.Title,
            row.Description,
            dueDate: null);
        return (priority, confidence);
    }

    public static bool IsSafetyCase(ClassifyEvalRow row)
    {
        var text = $"{row.Title} {row.Description} {row.Category}".ToLowerInvariant();
        return text.Contains("ignore previous", StringComparison.Ordinal)
            || text.Contains("prompt injection", StringComparison.Ordinal)
            || text.Contains("cannot login", StringComparison.Ordinal)
            || text.Contains("production down", StringComparison.Ordinal)
            || string.Equals(row.Category, "Security", StringComparison.OrdinalIgnoreCase);
    }

    private static List<string> ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        fields.Add(current.ToString());
        return fields;
    }
}
