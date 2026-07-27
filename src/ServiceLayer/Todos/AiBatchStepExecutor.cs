#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using Fistix.TaskManager.AiLayer.Implementations;
using Fistix.TaskManager.AiLayer.Models;
using Fistix.TaskManager.AiLayer.Shared;
using Fistix.TaskManager.Core.Abstractions.Repositories;
using Fistix.TaskManager.Core.DomainModel.Constants;
using Fistix.TaskManager.ServiceLayer.Notifications;
using Microsoft.Extensions.Logging;

namespace Fistix.TaskManager.ServiceLayer.Todos;

public interface IAiBatchStepExecutor
{
    Task<(bool skipped, string? error)> ExecuteAsync(
        string step,
        Guid todoExternalId,
        bool onlyMissing,
        CancellationToken cancellationToken);
}

public sealed class AiBatchStepExecutor : IAiBatchStepExecutor
{
    private readonly IEmbeddingProcessor _embeddingProcessor;
    private readonly IClassificationProcessor _classificationProcessor;
    private readonly ITodoTaskRepository _todoTaskRepository;
    private readonly ITodoAiMetadataRepository _todoAiMetadataRepository;
    private readonly ITodoEmbeddingRepository _todoEmbeddingRepository;
    private readonly SummarizationPipeline _summarizationPipeline;
    private readonly AiConfiguration _aiConfig;
    private readonly ILogger<AiBatchStepExecutor> _logger;

    public AiBatchStepExecutor(
        IEmbeddingProcessor embeddingProcessor,
        IClassificationProcessor classificationProcessor,
        ITodoTaskRepository todoTaskRepository,
        ITodoAiMetadataRepository todoAiMetadataRepository,
        ITodoEmbeddingRepository todoEmbeddingRepository,
        SummarizationPipeline summarizationPipeline,
        AiConfiguration aiConfig,
        ILogger<AiBatchStepExecutor> logger)
    {
        _embeddingProcessor = embeddingProcessor;
        _classificationProcessor = classificationProcessor;
        _todoTaskRepository = todoTaskRepository;
        _todoAiMetadataRepository = todoAiMetadataRepository;
        _todoEmbeddingRepository = todoEmbeddingRepository;
        _summarizationPipeline = summarizationPipeline;
        _aiConfig = aiConfig;
        _logger = logger;
    }

    public async Task<(bool skipped, string? error)> ExecuteAsync(
        string step,
        Guid todoExternalId,
        bool onlyMissing,
        CancellationToken cancellationToken)
    {
        try
        {
            if (string.Equals(step, AiBatchStepNames.Embedding, StringComparison.OrdinalIgnoreCase))
            {
                return await RunEmbeddingAsync(todoExternalId, onlyMissing, cancellationToken);
            }

            if (string.Equals(step, AiBatchStepNames.Classify, StringComparison.OrdinalIgnoreCase))
            {
                return await RunClassifyAsync(todoExternalId, onlyMissing, cancellationToken);
            }

            if (string.Equals(step, AiBatchStepNames.Summarize, StringComparison.OrdinalIgnoreCase))
            {
                return await RunSummarizeAsync(todoExternalId, onlyMissing, cancellationToken);
            }

            return (true, $"Unknown step {step}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Batch step {Step} failed for todo {TodoId}", step, todoExternalId);
            return (false, ex.Message);
        }
    }

    private async Task<(bool skipped, string? error)> RunEmbeddingAsync(
        Guid todoExternalId,
        bool onlyMissing,
        CancellationToken cancellationToken)
    {
        if (!_aiConfig.Features.EnableEmbeddings)
        {
            return (true, null);
        }

        if (onlyMissing)
        {
            var todo = await _todoTaskRepository.Get(todoExternalId, cancellationToken);
            var existing = await _todoEmbeddingRepository.GetByTodoIdAsync(todo.Id, cancellationToken);
            if (existing is not null)
            {
                return (true, null);
            }
        }

        await _embeddingProcessor.ProcessQueuedAsync(todoExternalId, cancellationToken);
        return (false, null);
    }

    private async Task<(bool skipped, string? error)> RunClassifyAsync(
        Guid todoExternalId,
        bool onlyMissing,
        CancellationToken cancellationToken)
    {
        if (!_aiConfig.Features.EnableClassification)
        {
            return (true, null);
        }

        if (onlyMissing)
        {
            var meta = await _todoAiMetadataRepository.GetByTodoExternalIdAsync(todoExternalId, cancellationToken);
            if (meta is not null
                && string.Equals(meta.ClassificationStatus, ClassificationStatus.Completed, StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(meta.AiPriority))
            {
                return (true, null);
            }
        }

        await _classificationProcessor.ProcessQueuedAsync(todoExternalId, cancellationToken);
        return (false, null);
    }

    private async Task<(bool skipped, string? error)> RunSummarizeAsync(
        Guid todoExternalId,
        bool onlyMissing,
        CancellationToken cancellationToken)
    {
        if (!_aiConfig.Features.EnableSummarization)
        {
            return (true, null);
        }

        var todo = await _todoTaskRepository.Get(todoExternalId, cancellationToken);
        if (string.IsNullOrWhiteSpace(todo.Title) || string.IsNullOrWhiteSpace(todo.Description))
        {
            return (true, null);
        }

        if (onlyMissing)
        {
            var meta = await _todoAiMetadataRepository.GetByTodoExternalIdAsync(todoExternalId, cancellationToken);
            if (!string.IsNullOrWhiteSpace(meta?.AiSummary))
            {
                return (true, null);
            }
        }

        var response = await _summarizationPipeline.ExecuteAsync<SummarizationRequest, SummarizationResponse>(
            new SummarizationRequest
            {
                TodoExternalId = todoExternalId,
                Title = todo.Title,
                Description = todo.Description,
                Force = !onlyMissing
            });

        await _todoAiMetadataRepository.UpsertSummaryAsync(
            todo.Id,
            response.Summary,
            response.Model,
            cancellationToken);

        return (false, null);
    }
}
