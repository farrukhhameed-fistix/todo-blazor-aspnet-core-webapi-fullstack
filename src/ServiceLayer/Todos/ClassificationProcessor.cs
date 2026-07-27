using Fistix.TaskManager.AiLayer.Implementations;
using Fistix.TaskManager.AiLayer.Models;
using Fistix.TaskManager.AiLayer.Shared;
using Fistix.TaskManager.Core.Abstractions.Repositories;
using Fistix.TaskManager.Core.DomainModel.Constants;
using Fistix.TaskManager.ServiceLayer.Notifications;
using Fistix.TaskManager.ViewModel.Dtos;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Fistix.TaskManager.ServiceLayer.Todos;

public interface IClassificationProcessor
{
    Task<TaskClassificationDto> ProcessAsync(Guid todoExternalId, bool force, CancellationToken cancellationToken);
    Task<TaskClassificationDto> ProcessQueuedAsync(Guid todoExternalId, CancellationToken cancellationToken);
}

public class ClassificationProcessor : IClassificationProcessor
{
    private readonly ITodoTaskRepository _todoTaskRepository;
    private readonly ITodoAiMetadataRepository _todoAiMetadataRepository;
    private readonly ClassificationPipeline _classificationPipeline;
    private readonly IClassificationNotifier _classificationNotifier;
    private readonly ILogger<ClassificationProcessor> _logger;

    public ClassificationProcessor(
        ITodoTaskRepository todoTaskRepository,
        ITodoAiMetadataRepository todoAiMetadataRepository,
        ClassificationPipeline classificationPipeline,
        IClassificationNotifier classificationNotifier,
        ILogger<ClassificationProcessor> logger)
    {
        _todoTaskRepository = todoTaskRepository;
        _todoAiMetadataRepository = todoAiMetadataRepository;
        _classificationPipeline = classificationPipeline;
        _classificationNotifier = classificationNotifier;
        _logger = logger;
    }

    public async Task<TaskClassificationDto> ProcessAsync(
        Guid todoExternalId,
        bool force,
        CancellationToken cancellationToken)
    {
        var todo = await _todoTaskRepository.Get(todoExternalId, cancellationToken);

        if (string.IsNullOrWhiteSpace(todo.Title))
        {
            throw new InvalidOperationException("Title is required to classify priority.");
        }

        if (!force)
        {
            var cachedMetadata = await _todoAiMetadataRepository.GetByTodoExternalIdAsync(todoExternalId, cancellationToken);
            if (cachedMetadata is not null)
            {
                if (string.Equals(cachedMetadata.ClassificationStatus, ClassificationStatus.Pending, StringComparison.Ordinal)
                    || string.Equals(cachedMetadata.ClassificationStatus, ClassificationStatus.Failed, StringComparison.Ordinal))
                {
                    return MapMetadata(todoExternalId, cachedMetadata, fromCache: true);
                }

                if (!string.IsNullOrWhiteSpace(cachedMetadata.AiPriority)
                    && string.Equals(cachedMetadata.ClassificationStatus, ClassificationStatus.Completed, StringComparison.Ordinal))
                {
                    return MapMetadata(todoExternalId, cachedMetadata, fromCache: true);
                }
            }
        }

        return await RunClassificationAsync(todo, todoExternalId, force, cancellationToken);
    }

    public async Task<TaskClassificationDto> ProcessQueuedAsync(
        Guid todoExternalId,
        CancellationToken cancellationToken)
    {
        var todo = await _todoTaskRepository.Get(todoExternalId, cancellationToken);

        if (string.IsNullOrWhiteSpace(todo.Title))
        {
            throw new InvalidOperationException("Title is required to classify priority.");
        }

        var cachedMetadata = await _todoAiMetadataRepository.GetByTodoExternalIdAsync(todoExternalId, cancellationToken);
        if (cachedMetadata is not null
            && !string.IsNullOrWhiteSpace(cachedMetadata.AiPriority)
            && string.Equals(cachedMetadata.ClassificationStatus, ClassificationStatus.Completed, StringComparison.Ordinal))
        {
            _logger.LogDebug("Skipping queued classification for todo {TodoExternalId}; already completed", todoExternalId);
            return MapMetadata(todoExternalId, cachedMetadata, fromCache: true);
        }

        _logger.LogInformation("Running queued classification for todo {TodoExternalId}", todoExternalId);
        return await RunClassificationAsync(todo, todoExternalId, force: false, cancellationToken);
    }

    private async Task<TaskClassificationDto> RunClassificationAsync(
        Core.DomainModel.Aggregates.TodoTask todo,
        Guid todoExternalId,
        bool force,
        CancellationToken cancellationToken)
    {
        await _todoAiMetadataRepository.MarkClassificationPendingAsync(todo.Id, cancellationToken);

        try
        {
            var request = new ClassificationRequest
            {
                TodoExternalId = todoExternalId,
                Title = todo.Title,
                Description = todo.Description ?? string.Empty,
                DueDate = todo.DueDate == default ? null : todo.DueDate,
                Force = force
            };

            var response = await _classificationPipeline
                .ExecuteAsync<ClassificationRequest, ClassificationResponse>(request);

            await _todoAiMetadataRepository.UpsertClassificationAsync(
                todo.Id,
                response.SuggestedPriority,
                response.Confidence,
                response.Reason,
                response.Model,
                cancellationToken);

            // Re-load task so priority edits made while classification was Pending are reflected.
            var latestTodo = await _todoTaskRepository.Get(todoExternalId, cancellationToken);
            var wasOverridden = ClassificationGuardrails.IsPriorityOverridden(
                latestTodo.Priority,
                response.SuggestedPriority);
            await _todoAiMetadataRepository.SetWasOverriddenAsync(todo.Id, wasOverridden, cancellationToken);

            var result = new TaskClassificationDto
            {
                TodoExternalId = response.TodoExternalId,
                SuggestedPriority = response.SuggestedPriority,
                Confidence = response.Confidence,
                Reason = response.Reason,
                Status = ClassificationStatus.Completed,
                Model = response.Model,
                FromCache = false,
                GeneratedAt = response.GeneratedAt
            };

            await TryNotifyAsync(result, cancellationToken);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Classification failed for todo {TodoExternalId}. RootCause: {RootCause}",
                todoExternalId,
                GetRootCause(ex));
            await _todoAiMetadataRepository.MarkClassificationFailedAsync(todo.Id, cancellationToken);

            var failed = new TaskClassificationDto
            {
                TodoExternalId = todoExternalId,
                Status = ClassificationStatus.Failed
            };
            await TryNotifyAsync(failed, cancellationToken);
            throw;
        }
    }

    private async Task TryNotifyAsync(TaskClassificationDto payload, CancellationToken cancellationToken)
    {
        try
        {
            await _classificationNotifier.NotifyAsync(payload, cancellationToken);
        }
        catch (Exception notifyEx)
        {
            _logger.LogWarning(
                notifyEx,
                "Failed to notify classification status {Status} for todo {TodoExternalId}",
                payload.Status,
                payload.TodoExternalId);
        }
    }

    private static TaskClassificationDto MapMetadata(Guid todoExternalId, Core.DomainModel.Aggregates.TodoAiMetadata metadata, bool fromCache)
    {
        return new TaskClassificationDto
        {
            TodoExternalId = todoExternalId,
            SuggestedPriority = metadata.AiPriority,
            Confidence = metadata.ConfidenceScore,
            Reason = metadata.AiPriorityReason,
            Status = metadata.ClassificationStatus ?? ClassificationStatus.None,
            Model = metadata.AiPriorityModel,
            FromCache = fromCache,
            GeneratedAt = metadata.UpdatedAt ?? metadata.CreatedAt
        };
    }

    private static string GetRootCause(Exception ex)
    {
        Exception current = ex;
        while (current.InnerException is not null)
        {
            current = current.InnerException;
        }

        return $"{current.GetType().Name}: {current.Message}";
    }
}
