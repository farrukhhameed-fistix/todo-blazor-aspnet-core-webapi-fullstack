#nullable enable

using Fistix.TaskManager.AiLayer.Abstractions;
using Fistix.TaskManager.AiLayer.Models;
using Fistix.TaskManager.AiLayer.Observability;
using Fistix.TaskManager.AiLayer.Shared;
using Microsoft.Extensions.Logging;
using System.Text;

namespace Fistix.TaskManager.AiLayer.Implementations;

/// <summary>
/// Generates an LLM answer from retrieved todo sources.
/// Retrieval is owned by the caller (e.g. SemanticSearchPipeline with BGE Query + MinSimilarity).
/// </summary>
public sealed class RAGPipeline
{
    private readonly ILlmProviderService _llm;
    private readonly AiConfiguration _aiConfig;
    private readonly IAiTelemetry _telemetry;
    private readonly ILogger<RAGPipeline> _logger;

    public RAGPipeline(
        ILlmProviderService llm,
        AiConfiguration aiConfig,
        ILogger<RAGPipeline> logger,
        IAiTelemetry? telemetry = null)
    {
        _llm = llm;
        _aiConfig = aiConfig;
        _logger = logger;
        _telemetry = telemetry ?? NullAiTelemetry.Instance;
    }

    public async Task<RagPipelineResult> ExecuteAsync(
        RagPipelineRequest request,
        CancellationToken cancellationToken = default)
    {
        var model = ResolveChatModel(_aiConfig);
        using var operation = _telemetry.StartOperation(
            AiTelemetryNames.Features.Rag,
            model: model,
            provider: _aiConfig.Provider);
        operation.Activity?.SetTag(AiTelemetryNames.Tags.PromptVersion, AiPromptVersions.Rag);

        var sourceIds = request.SourceTodos.Select(s => s.ExternalId).ToList();

        try
        {
            if (request.SourceTodos.Count == 0)
            {
                operation.SetOutcome(AiTelemetryNames.Outcomes.InsufficientContext);
                _telemetry.RecordQualityEvent(
                    AiTelemetryNames.Features.Rag,
                    AiTelemetryNames.QualityEvents.InsufficientContext);

                return new RagPipelineResult
                {
                    Answer = LlmOutputValidator.InsufficientContextMessage,
                    SourceTodoIds = sourceIds,
                    Model = model
                };
            }

            var sanitizedQuestion = PromptInputSanitizer.SanitizeAndTruncate(
                request.Question, LlmInputLimits.ToolSearchQueryMaxLength * 4);
            if (string.IsNullOrWhiteSpace(sanitizedQuestion))
            {
                operation.SetOutcome(AiTelemetryNames.Outcomes.ValidationFailed);
                _telemetry.RecordQualityEvent(
                    AiTelemetryNames.Features.Rag,
                    AiTelemetryNames.QualityEvents.ValidationFailed);

                return new RagPipelineResult
                {
                    Answer = LlmOutputValidator.InsufficientContextMessage,
                    SourceTodoIds = sourceIds,
                    Model = model
                };
            }

            var todayUtc = DateTime.UtcNow.Date;
            var contextBuilder = new StringBuilder();
            contextBuilder.AppendLine($"Today's date (UTC): {todayUtc:yyyy-MM-dd}");
            if (!string.IsNullOrWhiteSpace(request.PreFilteredDateWindow))
            {
                contextBuilder.AppendLine(
                    $"Due-date filter already applied by the system: {PromptInputSanitizer.SanitizeAndTruncate(request.PreFilteredDateWindow, 128)}. " +
                    "Every task below is already inside that window — do not expand or shrink the calendar range.");
            }

            if (!string.IsNullOrWhiteSpace(request.PreFilteredPriority))
            {
                contextBuilder.AppendLine(
                    $"Priority filter already applied by the system: {PromptInputSanitizer.SanitizeAndTruncate(request.PreFilteredPriority, 32)}. " +
                    "Do not include tasks of other priorities.");
            }

            if (request.IsAdviceQuestion)
            {
                contextBuilder.AppendLine(
                    "Tasks below are pre-sorted for advice (High → earlier due). Recommend in this order.");
            }

            foreach (var source in request.SourceTodos)
            {
                if (contextBuilder.Length >= LlmInputLimits.RagTotalContextMaxLength)
                {
                    break;
                }

                var title = PromptInputSanitizer.SanitizeAndTruncate(source.Title, LlmInputLimits.TitleMaxLength);
                var description = PromptInputSanitizer.SanitizeAndTruncate(
                    source.Description, LlmInputLimits.RagContextDescriptionMaxLength);

                contextBuilder.AppendLine(
                    $"- [{source.ExternalId}] {title} | priority={source.Priority} status={source.Status} due={source.DueDate:u}");
                if (!string.IsNullOrWhiteSpace(description))
                {
                    var remaining = LlmInputLimits.RagTotalContextMaxLength - contextBuilder.Length;
                    if (remaining <= 0)
                    {
                        break;
                    }

                    var clipped = description.Length <= remaining ? description : description[..remaining];
                    contextBuilder.AppendLine($"  {clipped}");
                }
            }

            var prefilteredLabel = string.IsNullOrWhiteSpace(request.PreFilteredDateWindow)
                ? null
                : PromptInputSanitizer.SanitizeAndTruncate(request.PreFilteredDateWindow, 128);

            var dateGuidance = prefilteredLabel is null
                ? $"""
                Today's date (UTC) is {todayUtc:yyyy-MM-dd}. Interpret relative time phrases such as "this week", "next month", or "this year" relative to that date and only against the task due dates in the context (the due= field).
                """
                : $"""
                Today's date (UTC) is {todayUtc:yyyy-MM-dd}. The task list is already filtered to: {prefilteredLabel}.
                Do not add or remove tasks based on calendar reasoning — only filter/rank/answer using fields on the provided tasks (title, description, and any criteria not already applied by the system).
                """;

            var listGuidance = """
                When listing or selecting tasks, cite ONLY tasks that match the user's topic and remaining criteria.
                Omit unrelated tasks that happen to appear in the context. If none match, say so clearly.
                """;

            var adviceGuidance = request.IsAdviceQuestion
                ? """
                The user asked what to work on next (advice). The task context is ALREADY ordered: higher priority first, then earlier due dates.
                Follow that order when recommending. Put overdue High Pending work first — never last.
                Overdue means due date is before today AND status is still open (e.g. Pending/InProgress). Overdue raises urgency; it does NOT mean done, obsolete, or "no longer in focus".
                Never say High Pending overdue tasks should be ignored, deprioritized, or treated as completed.
                Do not invent or change status fields. Only use status= from the context.
                Prefer concrete domain work (Auth, Payments, etc.). Skip meta/planning tasks that only collect or estimate work for an optimizer unless the question is about sprint planning itself.
                If the question names multiple domains (e.g. Auth and Payments), include at least one strong matching task from EACH domain when such tasks appear in the context.
                Priority is ordering only: if no High tasks appear in the context, recommend the best Medium/Low open tasks that are listed — do NOT say there is nothing to work on while Pending tasks remain in the context.
                Only say there is nothing to do when the task context is empty.
                Be concise: recommend the top matching tasks with titles and ids in priority/due order, covering every named domain when possible.
                """
                : string.Empty;

            var prompt = $"""
                You are a task-management assistant. Answer the user's question using ONLY the provided task context.
                If the context is insufficient, say what is missing. Be concise and cite task titles with their ids.
                {dateGuidance}
                Do not invent todo GUIDs. Only reference ids that appear in the task context.
                {listGuidance}
                {adviceGuidance}

                Task context:
                {contextBuilder}

                Question: {sanitizedQuestion}
                """;

            _logger.LogInformation("Running RAG with {Count} sources", request.SourceTodos.Count);
            var answer = await _llm.GetCompletionAsync(prompt, cancellationToken);

            try
            {
                var validated = LlmOutputValidator.ValidateRagAnswer(answer, sourceIds);
                operation.SetOutcome(AiTelemetryNames.Outcomes.Success);
                return new RagPipelineResult
                {
                    Answer = validated,
                    SourceTodoIds = sourceIds,
                    Model = model
                };
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogWarning(ex, "RAG answer failed faithfulness validation");
                operation.SetOutcome(AiTelemetryNames.Outcomes.ValidationFailed);
                _telemetry.RecordQualityEvent(
                    AiTelemetryNames.Features.Rag,
                    AiTelemetryNames.QualityEvents.UngroundedAnswer);

                return new RagPipelineResult
                {
                    Answer = LlmOutputValidator.UngroundedAnswerMessage,
                    SourceTodoIds = sourceIds,
                    Model = model
                };
            }
        }
        catch
        {
            operation.SetOutcome(AiTelemetryNames.Outcomes.Error);
            throw;
        }
    }

    /// <summary>Chat/LLM model that produced the answer (not the embedding model).</summary>
    public static string ResolveChatModel(AiConfiguration aiConfig)
    {
        var provider = (aiConfig.Provider ?? string.Empty).Trim().ToLowerInvariant();
        var model = provider switch
        {
            "google" => aiConfig.GoogleAI.Model,
            "openai" => aiConfig.OpenAI.Model,
            "azureopenai" => aiConfig.AzureOpenAI.Model,
            "claude" => aiConfig.Claude.Model,
            "ollama" => aiConfig.Ollama.Model,
            _ => null
        };

        return string.IsNullOrWhiteSpace(model)
            ? (string.IsNullOrWhiteSpace(aiConfig.Provider) ? "unknown" : aiConfig.Provider)
            : model;
    }
}
