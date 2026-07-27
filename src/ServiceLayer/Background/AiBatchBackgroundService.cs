#nullable enable

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fistix.TaskManager.AiLayer.Shared;
using Fistix.TaskManager.Core.Abstractions.Repositories;
using Fistix.TaskManager.Core.DomainModel.Constants;
using Fistix.TaskManager.ServiceLayer.Notifications;
using Fistix.TaskManager.ServiceLayer.Todos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Fistix.TaskManager.ServiceLayer.Background;

/// <summary>
/// Processes durable AI batch jobs with pause/continue/cancel and heartbeat/stuck detection.
/// </summary>
public sealed class AiBatchBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AiConfiguration _aiConfig;
    private readonly ILogger<AiBatchBackgroundService> _logger;

    public AiBatchBackgroundService(
        IServiceScopeFactory scopeFactory,
        AiConfiguration aiConfig,
        ILogger<AiBatchBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _aiConfig = aiConfig;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("AI batch background service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await MarkStuckJobsAsync(stoppingToken);
                var processed = await ProcessOneTickAsync(stoppingToken);
                if (!processed)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AI batch worker loop error");
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
        }
    }

    private async Task MarkStuckJobsAsync(CancellationToken cancellationToken)
    {
        var stuckAfterSeconds = Math.Max(30, _aiConfig.Features.Batch.StuckAfterSeconds);
        using var scope = _scopeFactory.CreateScope();
        var jobs = scope.ServiceProvider.GetRequiredService<IAiBatchJobRepository>();
        var notifier = scope.ServiceProvider.GetRequiredService<IAiBatchNotifier>();
        var stale = await jobs.GetStaleRunningAsync(TimeSpan.FromSeconds(stuckAfterSeconds), cancellationToken);
        foreach (var job in stale)
        {
            job.Status = AiBatchJobStatus.Stuck;
            job.LastError = $"No heartbeat for more than {stuckAfterSeconds}s. Cancel or Continue from UI.";
            await jobs.UpdateAsync(job, cancellationToken);
            await notifier.NotifyAsync(AiBatchJobMapper.ToDto(job), cancellationToken);
            _logger.LogWarning("Marked AI batch job {JobId} as Stuck", job.ExternalId);
        }
    }

    private async Task<bool> ProcessOneTickAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var jobs = scope.ServiceProvider.GetRequiredService<IAiBatchJobRepository>();
        var executor = scope.ServiceProvider.GetRequiredService<IAiBatchStepExecutor>();
        var notifier = scope.ServiceProvider.GetRequiredService<IAiBatchNotifier>();

        var runnable = await jobs.GetRunnableAsync(cancellationToken);
        var job = runnable.FirstOrDefault(j => j.Status == AiBatchJobStatus.Running)
                  ?? runnable.FirstOrDefault(j => j.Status == AiBatchJobStatus.Pending);

        if (job is null)
        {
            return false;
        }

        if (job.Status == AiBatchJobStatus.Pending)
        {
            job.Status = AiBatchJobStatus.Running;
            job.HeartbeatAt = DateTime.UtcNow;
            await jobs.UpdateAsync(job, cancellationToken);
        }

        // Re-read to honor pause/cancel from UI
        job = await jobs.GetByExternalIdAsync(job.ExternalId, cancellationToken);
        if (job is null || job.Status != AiBatchJobStatus.Running || job.CancelRequested)
        {
            return false;
        }

        var todoIds = AiBatchJobMapper.DeserializeIds(job.TodoExternalIdsJson);
        var steps = AiBatchJobMapper.ParseSteps(job.StepsCsv);
        if (todoIds.Count == 0 || steps.Count == 0)
        {
            job.Status = AiBatchJobStatus.Completed;
            job.CompletedAt = DateTime.UtcNow;
            await jobs.UpdateAsync(job, cancellationToken);
            await notifier.NotifyAsync(AiBatchJobMapper.ToDto(job), cancellationToken);
            return true;
        }

        var stepIndex = steps.FindIndex(s =>
            string.Equals(s, job.CurrentStep, StringComparison.OrdinalIgnoreCase));
        if (stepIndex < 0)
        {
            stepIndex = 0;
            job.CurrentStep = steps[0];
        }

        if (job.Cursor >= todoIds.Count)
        {
            if (stepIndex + 1 >= steps.Count)
            {
                job.Status = AiBatchJobStatus.Completed;
                job.CompletedAt = DateTime.UtcNow;
                job.Cursor = todoIds.Count;
                await jobs.UpdateAsync(job, cancellationToken);
                await notifier.NotifyAsync(AiBatchJobMapper.ToDto(job), cancellationToken);
                return true;
            }

            job.CurrentStep = steps[stepIndex + 1];
            job.Cursor = 0;
            job.HeartbeatAt = DateTime.UtcNow;
            await jobs.UpdateAsync(job, cancellationToken);
            await notifier.NotifyAsync(AiBatchJobMapper.ToDto(job), cancellationToken);
            return true;
        }

        var batchSize = Math.Max(1, job.BatchSize);
        var end = Math.Min(job.Cursor + batchSize, todoIds.Count);

        for (var i = job.Cursor; i < end; i++)
        {
            // Honor pause/cancel between items
            var latest = await jobs.GetByExternalIdAsync(job.ExternalId, cancellationToken);
            if (latest is null)
            {
                return true;
            }

            if (latest.CancelRequested || latest.Status == AiBatchJobStatus.Cancelled)
            {
                latest.Status = AiBatchJobStatus.Cancelled;
                latest.CompletedAt = DateTime.UtcNow;
                await jobs.UpdateAsync(latest, cancellationToken);
                await notifier.NotifyAsync(AiBatchJobMapper.ToDto(latest), cancellationToken);
                return true;
            }

            if (latest.Status == AiBatchJobStatus.Paused)
            {
                return true;
            }

            job = latest;
            var todoId = todoIds[i];
            job.LastTodoExternalId = todoId;
            job.HeartbeatAt = DateTime.UtcNow;
            await jobs.UpdateAsync(job, cancellationToken);

            var itemTimeout = TimeSpan.FromMilliseconds(
                Math.Max(5_000, _aiConfig.Features.Batch.ItemTimeoutMs));
            using var itemCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            itemCts.CancelAfter(itemTimeout);

            try
            {
                var (skipped, error) = await executor.ExecuteAsync(
                    job.CurrentStep,
                    todoId,
                    job.OnlyMissing,
                    itemCts.Token);

                if (!string.IsNullOrWhiteSpace(error))
                {
                    job.Failed++;
                    job.LastError = error;
                }
                else if (skipped)
                {
                    job.Skipped++;
                }
                else
                {
                    job.Completed++;
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                job.Failed++;
                job.LastError = $"Timed out after {itemTimeout.TotalSeconds:0}s on {job.CurrentStep}";
            }

            job.Cursor = i + 1;
            job.HeartbeatAt = DateTime.UtcNow;
            await jobs.UpdateAsync(job, cancellationToken);
            await notifier.NotifyAsync(AiBatchJobMapper.ToDto(job), cancellationToken);

            if (job.DelayMsBetweenItems > 0)
            {
                await Task.Delay(job.DelayMsBetweenItems, cancellationToken);
            }
        }

        return true;
    }
}
