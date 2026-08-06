#nullable enable

using Fistix.TaskManager.AiLayer.Abstractions;
using Fistix.TaskManager.AiLayer.Shared;

namespace Fistix.TaskManager.AiLayer.Evaluation;

/// <summary>Offline-only LLM judge. Must not be used on the production RAG hot path.</summary>
public interface ILlmJudge
{
    Task<LlmJudgeResult> JudgeAsync(LlmJudgeRequest request, CancellationToken cancellationToken = default);
}

public sealed class LlmJudgeRequest
{
    public string Question { get; init; } = string.Empty;
    public string Context { get; init; } = string.Empty;
    public string Answer { get; init; } = string.Empty;
    public string Rubric { get; init; } = "faithfulness_and_relevance";
}

public sealed class LlmJudgeResult
{
    public int FaithfulnessScore { get; init; }
    public int AnswerRelevanceScore { get; init; }
    public string Rationale { get; init; } = string.Empty;
    public string RawResponse { get; init; } = string.Empty;
}

public sealed class LlmJudgeService : ILlmJudge
{
    private readonly ILlmProviderService _llm;

    public LlmJudgeService(ILlmProviderService llm)
    {
        _llm = llm;
    }

    public async Task<LlmJudgeResult> JudgeAsync(
        LlmJudgeRequest request,
        CancellationToken cancellationToken = default)
    {
        var prompt = $$"""
            You are an evaluation judge. Score the candidate answer.
            Return ONLY JSON: {"faithfulness":1-5,"answer_relevance":1-5,"rationale":"short"}
            faithfulness = supported by context only (5 = fully grounded).
            answer_relevance = addresses the question (5 = fully on-topic).

            <question>
            {{PromptInputSanitizer.SanitizeAndTruncate(request.Question, 2000)}}
            </question>
            <context>
            {{PromptInputSanitizer.SanitizeAndTruncate(request.Context, 6000)}}
            </context>
            <answer>
            {{PromptInputSanitizer.SanitizeAndTruncate(request.Answer, 4000)}}
            </answer>
            """;

        var raw = await _llm.GetCompletionAsync(prompt, cancellationToken);
        return Parse(raw);
    }

    public static LlmJudgeResult Parse(string raw)
    {
        try
        {
            var json = ExtractJsonObject(raw);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;
            var faithfulness = root.TryGetProperty("faithfulness", out var f) && f.TryGetInt32(out var fi) ? fi : 0;
            var relevance = root.TryGetProperty("answer_relevance", out var r) && r.TryGetInt32(out var ri) ? ri : 0;
            var rationale = root.TryGetProperty("rationale", out var ra) ? ra.GetString() ?? string.Empty : string.Empty;

            return new LlmJudgeResult
            {
                FaithfulnessScore = Math.Clamp(faithfulness, 1, 5),
                AnswerRelevanceScore = Math.Clamp(relevance, 1, 5),
                Rationale = LlmOutputValidator.TruncateReason(rationale) ?? string.Empty,
                RawResponse = raw
            };
        }
        catch
        {
            return new LlmJudgeResult
            {
                FaithfulnessScore = 1,
                AnswerRelevanceScore = 1,
                Rationale = "Judge response could not be parsed.",
                RawResponse = raw
            };
        }
    }

    private static string ExtractJsonObject(string raw)
    {
        var trimmed = raw.Trim();
        var start = trimmed.IndexOf('{');
        var end = trimmed.LastIndexOf('}');
        if (start >= 0 && end > start)
        {
            return trimmed[start..(end + 1)];
        }

        throw new InvalidOperationException("No JSON object in judge response.");
    }
}
