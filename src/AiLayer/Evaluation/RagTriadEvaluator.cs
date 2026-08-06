#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Fistix.TaskManager.AiLayer.Shared;

namespace Fistix.TaskManager.AiLayer.Evaluation;

public sealed class RagEvalCase
{
    public string Id { get; set; } = string.Empty;
    public string Question { get; set; } = string.Empty;
    public List<string> ExpectedSourceTitles { get; set; } = [];
    public bool ExpectInsufficient { get; set; }
    public string? Notes { get; set; }
}

public sealed class RagTriadScores
{
    public string CaseId { get; init; } = string.Empty;
    public double ContextRelevance { get; init; }
    public bool FaithfulnessPass { get; init; }
    public int? AnswerRelevanceJudge { get; init; }
    public int? FaithfulnessJudge { get; init; }
}

/// <summary>Offline RAG Triad helpers (heuristic legs; judge optional via ILlmJudge).</summary>
public static class RagTriadEvaluator
{
    public static IReadOnlyList<RagEvalCase> LoadFixtures(string jsonPath)
    {
        var json = System.IO.File.ReadAllText(jsonPath);
        return JsonSerializer.Deserialize<List<RagEvalCase>>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? [];
    }

    /// <summary>
    /// Context relevance ≈ recall@k of expected titles among retrieved titles (case-insensitive contains).
    /// </summary>
    public static double ScoreContextRelevance(
        IReadOnlyList<string> expectedTitles,
        IReadOnlyList<string> retrievedTitles)
    {
        if (expectedTitles.Count == 0)
        {
            return retrievedTitles.Count == 0 ? 1.0 : 0.0;
        }

        var hits = expectedTitles.Count(expected =>
            retrievedTitles.Any(retrieved =>
                retrieved.Contains(expected, StringComparison.OrdinalIgnoreCase) ||
                expected.Contains(retrieved, StringComparison.OrdinalIgnoreCase)));

        return (double)hits / expectedTitles.Count;
    }

    public static bool ScoreFaithfulnessHeuristic(string answer, IReadOnlyCollection<Guid> sourceIds)
    {
        try
        {
            _ = LlmOutputValidator.ValidateRagAnswer(answer, sourceIds);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public static RagTriadScores ScoreCase(
        RagEvalCase evalCase,
        IReadOnlyList<string> retrievedTitles,
        string answer,
        IReadOnlyCollection<Guid> sourceIds,
        LlmJudgeResult? judge = null)
    {
        if (evalCase.ExpectInsufficient)
        {
            var insufficient =
                string.Equals(answer, LlmOutputValidator.InsufficientContextMessage, StringComparison.Ordinal) ||
                sourceIds.Count == 0;
            return new RagTriadScores
            {
                CaseId = evalCase.Id,
                ContextRelevance = sourceIds.Count == 0 ? 1.0 : 0.0,
                FaithfulnessPass = insufficient,
                AnswerRelevanceJudge = judge?.AnswerRelevanceScore,
                FaithfulnessJudge = judge?.FaithfulnessScore
            };
        }

        return new RagTriadScores
        {
            CaseId = evalCase.Id,
            ContextRelevance = ScoreContextRelevance(evalCase.ExpectedSourceTitles, retrievedTitles),
            FaithfulnessPass = ScoreFaithfulnessHeuristic(answer, sourceIds),
            AnswerRelevanceJudge = judge?.AnswerRelevanceScore,
            FaithfulnessJudge = judge?.FaithfulnessScore
        };
    }
}
