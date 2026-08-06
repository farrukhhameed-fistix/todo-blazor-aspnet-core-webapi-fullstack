#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Fistix.TaskManager.AiLayer.Shared;
using Fistix.TaskManager.AiLayer.Tools;

namespace Fistix.TaskManager.AiLayer.Evaluation;

public sealed class ToolProposalEvalCase
{
    public string Id { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
    public List<string> ExpectedToolNames { get; set; } = [];
    public Dictionary<string, List<string>> RequiredArgKeys { get; set; } = new();
}

public sealed class ToolProposalEvalResult
{
    public string CaseId { get; init; } = string.Empty;
    public bool ToolNamesMatch { get; init; }
    public bool SchemaPassRateOk { get; init; }
    public double SchemaPassRate { get; init; }
}

public static class ToolProposalEvalHarness
{
    public static IReadOnlyList<ToolProposalEvalCase> LoadFixtures(string jsonPath)
    {
        var json = System.IO.File.ReadAllText(jsonPath);
        return JsonSerializer.Deserialize<List<ToolProposalEvalCase>>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? [];
    }

    public static ToolProposalEvalResult Score(
        ToolProposalEvalCase evalCase,
        IReadOnlyList<(string ToolName, Dictionary<string, JsonElement> Args)> proposed)
    {
        var expected = evalCase.ExpectedToolNames
            .Select(TodoToolDefinitions.NormalizeName)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var actual = proposed
            .Select(p => TodoToolDefinitions.NormalizeName(p.ToolName))
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var namesMatch = expected.Count == actual.Count &&
                         expected.Zip(actual).All(pair =>
                             string.Equals(pair.First, pair.Second, StringComparison.OrdinalIgnoreCase));

        var valid = 0;
        foreach (var (toolName, args) in proposed)
        {
            if (ToolArgumentValidator.Validate(toolName, args).IsValid)
            {
                valid++;
            }
        }

        var rate = proposed.Count == 0 ? 1.0 : (double)valid / proposed.Count;

        return new ToolProposalEvalResult
        {
            CaseId = evalCase.Id,
            ToolNamesMatch = namesMatch,
            SchemaPassRate = rate,
            SchemaPassRateOk = rate >= 1.0
        };
    }
}
