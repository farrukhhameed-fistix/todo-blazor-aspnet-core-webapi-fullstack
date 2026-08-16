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

            var todayDate = DateTime.UtcNow.Date;
            var today = todayDate.ToString("yyyy-MM-dd");
            var todayWeekday = todayDate.DayOfWeek.ToString();
            var systemPrompt = $$"""
                You are a task-management function-calling assistant.
                Given the user request, propose zero or more tool calls. Do NOT execute anything.
                Today (UTC) is {{today}} ({{todayWeekday}}).
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
                - Prefer index (1-based visible grid row) over id. Do not invent GUIDs when the user said a row number.
                - Prefer one-shot update_todo / mark_complete / set_priority over open→edit→save unless Current open task (edit) is provided.
                - For search_todos: put topic words in query; map status words to status; map relative dates to dueFrom/dueTo using Today. "show all my tasks" / "clear search" / "reset filters" -> search_todos with no query/status/dueFrom/dueTo.
                - Prefer semantic:true when the user asks about a topic in natural language (e.g. "regarding stripe").
                - delete/remove/done → mark_complete (no hard delete).
                - If Current open task context is provided and the user says this/it/the task without a number, omit index and omit id.
                - When Current open task (edit) is provided, map spoken title, description, due date, and/or priority to update_todo or set_priority (omit index/id). Do not call save_edit unless the user asked to save.
                - For create_todo/update_todo dueDate: resolve relative phrases from Today. "next Sunday" / "coming Sunday" = the next Sunday strictly after today (never invent a mid-week date). "tomorrow" = Today+1. Use YYYY-MM-DD.
                - Short command phrases should map to UI tools:
                  - "edit it", "start edit" -> start_edit
                  - "save it", "save changes" -> save_edit
                  - "close it", "cancel" -> close_todo or cancel_edit based on open/edit context
                  - "regenerate the priority" / "read the priority" -> regenerate_priority
                  - "regenerate the summary" / "regenerate the sunday of this task" -> regenerate_summary
                  - "set the priority to medium/high/low" -> set_priority or update_todo with priority
                  - "set due date last friday" / "sunday last friday" -> update_todo with dueDate for last Friday
                  - "set the title/description to …" -> update_todo with that field
                  - "show all my tasks" / "list all tasks" / "clear search" -> search_todos with empty arguments
                - Treat likely STT slips conservatively. Example: if edit context is open and transcript says "added", prefer edit intent.
                - Prefer "summary" over "sunday" when the user asks to regenerate something of a task.
                - Prefer "due date" over a stray weekday when another weekday already appears (e.g. "sunday last friday").
                """;

            var fullPrompt = $"""
                {systemPrompt}

                User request:
                {sanitizedPrompt}
                """;

            _logger.LogInformation("Proposing AI tool calls for prompt length {Length}", sanitizedPrompt.Length);
            var raw = await _llm.GetCompletionAsync(fullPrompt, cancellationToken);
            var parsed = ParseResponse(raw);

            var allowedCalls = new List<ProposedToolCall>();
            foreach (var c in parsed.Calls.Where(c => TodoToolDefinitions.IsAllowed(c.ToolName)))
            {
                var toolName = TodoToolDefinitions.NormalizeName(c.ToolName);
                var args = c.Arguments ?? new Dictionary<string, JsonElement>();
                var validation = ToolArgumentValidator.Validate(toolName, args);
                if (!validation.IsValid)
                {
                    _logger.LogWarning(
                        "Dropping proposed tool {ToolName}: {Error}",
                        toolName,
                        validation.Error);
                    _telemetry.RecordQualityEvent(
                        AiTelemetryNames.Features.ProposeTools,
                        AiTelemetryNames.QualityEvents.ToolArgRejected);
                    continue;
                }

                allowedCalls.Add(new ProposedToolCall
                {
                    ToolName = toolName,
                    Arguments = args
                });
            }

            // Deterministic correction: LLM often mis-dates "coming Sunday".
            RelativeDueDateResolver.ApplyToProposedCalls(sanitizedPrompt, allowedCalls, todayDate);

            operation.Activity?.SetTag(AiTelemetryNames.Tags.PromptVersion, AiPromptVersions.ProposeTools);
            operation.SetOutcome(AiTelemetryNames.Outcomes.Success);

            var explanation = string.IsNullOrWhiteSpace(parsed.Explanation)
                ? "Proposed tool calls based on your request."
                : parsed.Explanation.Trim();
            if (explanation.Length > LlmInputLimits.ExplanationMaxLength)
            {
                explanation = explanation[..LlmInputLimits.ExplanationMaxLength];
            }

            return new ToolProposalPipelineResult
            {
                Explanation = explanation,
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
