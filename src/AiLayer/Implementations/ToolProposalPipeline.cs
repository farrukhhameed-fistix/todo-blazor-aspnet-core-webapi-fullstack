#nullable enable

using Fistix.TaskManager.AiLayer.Abstractions;
using Fistix.TaskManager.AiLayer.Models;
using Fistix.TaskManager.AiLayer.Observability;
using Fistix.TaskManager.AiLayer.Shared;
using Fistix.TaskManager.AiLayer.Tools;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Fistix.TaskManager.AiLayer.Implementations;

/// <summary>
/// Asks the LLM to propose tool calls as JSON (user must confirm before execution).
/// </summary>
public sealed class ToolProposalPipeline
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ILlmProviderService _llm;
    private readonly IAiTelemetry _telemetry;
    private readonly ILogger<ToolProposalPipeline> _logger;

    public ToolProposalPipeline(
        ILlmProviderService llm,
        ILogger<ToolProposalPipeline> logger,
        IAiTelemetry? telemetry = null)
    {
        _llm = llm;
        _logger = logger;
        _telemetry = telemetry ?? NullAiTelemetry.Instance;
    }

    public async Task<ToolProposalPipelineResult> ExecuteAsync(
        ToolProposalPipelineRequest request,
        CancellationToken cancellationToken = default)
    {
        using var operation = _telemetry.StartOperation(AiTelemetryNames.Features.ProposeTools);

        try
        {
            var sanitizedPrompt = PromptInputSanitizer.SanitizeAndTruncate(request.Prompt, 2000);

            var systemPrompt = $$"""
                You are a task-management function-calling assistant.
                Given the user request, propose zero or more tool calls. Do NOT execute anything.
                {{TodoToolDefinitions.BuildCatalogForPrompt()}}

                Respond with ONLY valid JSON in this shape:
                {
                  "explanation": "short human-readable summary of what you intend to do",
                  "calls": [
                    {
                      "toolName": "create_todo",
                      "arguments": { "title": "...", "description": "...", "priority": "High" }
                    }
                  ]
                }

                Rules:
                - Use only the listed tool names.
                - Prefer the fewest calls that satisfy the request.
                - If the request cannot be mapped to tools, return an empty calls array and explain why.
                - For ids use GUID strings when the user provided them.
                """;

            var fullPrompt = $"""
                {systemPrompt}

                User request:
                {sanitizedPrompt}
                """;

            _logger.LogInformation("Proposing AI tool calls for prompt length {Length}", sanitizedPrompt.Length);
            var raw = await _llm.GetCompletionAsync(fullPrompt, cancellationToken);
            var parsed = ParseResponse(raw);

            var allowedCalls = parsed.Calls
                .Where(c => TodoToolDefinitions.IsAllowed(c.ToolName))
                .Select(c => new ProposedToolCall
                {
                    ToolName = TodoToolDefinitions.NormalizeName(c.ToolName),
                    Arguments = c.Arguments ?? new Dictionary<string, JsonElement>()
                })
                .ToList();

            operation.SetOutcome(AiTelemetryNames.Outcomes.Success);
            return new ToolProposalPipelineResult
            {
                Explanation = string.IsNullOrWhiteSpace(parsed.Explanation)
                    ? "Proposed tool calls based on your request."
                    : parsed.Explanation.Trim(),
                ProposedCalls = allowedCalls,
                Model = "function-calling"
            };
        }
        catch
        {
            operation.SetOutcome(AiTelemetryNames.Outcomes.Error);
            throw;
        }
    }

    private LlmToolProposalResponse ParseResponse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new LlmToolProposalResponse
            {
                Explanation = "No tool calls proposed (empty model response).",
                Calls = []
            };
        }

        var json = ExtractJsonObject(raw);
        try
        {
            var parsed = JsonSerializer.Deserialize<LlmToolProposalResponse>(json, JsonOptions);
            if (parsed is null)
            {
                throw new JsonException("Deserialized proposal was null.");
            }

            parsed.Calls ??= [];
            return parsed;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse tool proposal JSON from LLM");
            return new LlmToolProposalResponse
            {
                Explanation = "Could not parse tool proposals from the model response.",
                Calls = []
            };
        }
    }

    private static string ExtractJsonObject(string raw)
    {
        var trimmed = raw.Trim();
        if (trimmed.StartsWith("{", StringComparison.Ordinal))
        {
            return trimmed;
        }

        var fenced = Regex.Match(trimmed, @"```(?:json)?\s*(\{[\s\S]*?\})\s*```", RegexOptions.IgnoreCase);
        if (fenced.Success)
        {
            return fenced.Groups[1].Value;
        }

        var start = trimmed.IndexOf('{');
        var end = trimmed.LastIndexOf('}');
        if (start >= 0 && end > start)
        {
            return trimmed[start..(end + 1)];
        }

        return trimmed;
    }

    private sealed class LlmToolProposalResponse
    {
        public string Explanation { get; set; } = string.Empty;
        public List<LlmProposedCall> Calls { get; set; } = [];
    }

    private sealed class LlmProposedCall
    {
        public string ToolName { get; set; } = string.Empty;
        public Dictionary<string, JsonElement>? Arguments { get; set; }
    }
}
