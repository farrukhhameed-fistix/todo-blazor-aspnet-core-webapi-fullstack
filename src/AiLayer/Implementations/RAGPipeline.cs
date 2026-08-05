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

        try
        {
            var todayUtc = DateTime.UtcNow.Date;
            var contextBuilder = new StringBuilder();
            contextBuilder.AppendLine($"Context focus: {request.Context}");
            contextBuilder.AppendLine($"Today's date (UTC): {todayUtc:yyyy-MM-dd}");
            foreach (var source in request.SourceTodos)
            {
                contextBuilder.AppendLine($"- [{source.ExternalId}] {source.Title} | priority={source.Priority} status={source.Status} due={source.DueDate:u}");
                if (!string.IsNullOrWhiteSpace(source.Description))
                {
                    contextBuilder.AppendLine($"  {source.Description.Trim()}");
                }
            }

            var prompt = $"""
                You are a task-management assistant. Answer the user's question using ONLY the provided task context.
                If the context is insufficient, say what is missing. Be concise and cite task titles.
                Today's date (UTC) is {todayUtc:yyyy-MM-dd}. Interpret relative time phrases such as "this week", "next month", or "this year" relative to that date and only against the task due dates in the context.

                Task context:
                {contextBuilder}

                Question: {request.Question}
                """;

            _logger.LogInformation("Running RAG for context {Context} with {Count} sources", request.Context, request.SourceTodos.Count);
            var answer = await _llm.GetCompletionAsync(prompt, cancellationToken);
            operation.SetOutcome(AiTelemetryNames.Outcomes.Success);

            return new RagPipelineResult
            {
                Answer = answer.Trim(),
                SourceTodoIds = request.SourceTodos.Select(s => s.ExternalId).ToList(),
                Model = model
            };
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
