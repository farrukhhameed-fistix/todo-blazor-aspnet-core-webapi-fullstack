#nullable enable

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fistix.TaskManager.AiLayer.Shared;
using Fistix.TaskManager.Core.Abstractions.Repositories;
using Fistix.TaskManager.Core.DomainModel.Aggregates;
using Fistix.TaskManager.Core.DomainModel.Constants;
using Fistix.TaskManager.ServiceLayer.Notifications;
using Fistix.TaskManager.ServiceLayer.Todos;
using Fistix.TaskManager.ViewModel.Commands.Todos;
using Fistix.TaskManager.ViewModel.Dtos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Fistix.TaskManager.ServiceLayer.Background;

/// <summary>
/// Runs durable sprint optimizer jobs and pushes phase updates over SignalR.
/// </summary>
public sealed class SprintOptimizerBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AiConfiguration _aiConfig;
    private readonly ILogger<SprintOptimizerBackgroundService> _logger;

    public SprintOptimizerBackgroundService(
        IServiceScopeFactory scopeFactory,
        AiConfiguration aiConfig,
        ILogger<SprintOptimizerBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _aiConfig = aiConfig;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Sprint optimizer background service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await MarkStuckJobsAsync(stoppingToken);
                var processed = await ProcessOneJobAsync(stoppingToken);
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
                _logger.LogError(ex, "Sprint optimizer worker loop error");
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
        }
    }

    private async Task MarkStuckJobsAsync(CancellationToken cancellationToken)
    {
        var stuckAfterSeconds = Math.Max(60, _aiConfig.Agents?.StuckAfterSeconds ?? 300);
        using var scope = _scopeFactory.CreateScope();
        var jobs = scope.ServiceProvider.GetRequiredService<ISprintOptimizerJobRepository>();
        var notifier = scope.ServiceProvider.GetRequiredService<ISprintOptimizerNotifier>();
        var stale = await jobs.GetStaleRunningAsync(TimeSpan.FromSeconds(stuckAfterSeconds), cancellationToken);
        foreach (var job in stale)
        {
            job.Status = AiBatchJobStatus.Stuck;
            job.LastError = $"No heartbeat for more than {stuckAfterSeconds}s. Cancel and retry.";
            job.StatusMessage = "Marked stuck (no heartbeat).";
            await jobs.UpdateAsync(job, cancellationToken);
            await notifier.NotifyAsync(SprintOptimizerJobMapper.ToDto(job), cancellationToken);
            _logger.LogWarning("Marked sprint optimizer job {JobId} as Stuck", job.ExternalId);
        }
    }

    private async Task<bool> ProcessOneJobAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var jobs = scope.ServiceProvider.GetRequiredService<ISprintOptimizerJobRepository>();
        var notifier = scope.ServiceProvider.GetRequiredService<ISprintOptimizerNotifier>();
        var agent = scope.ServiceProvider.GetRequiredService<SprintOptimizerAgent>();
        var planningTools = scope.ServiceProvider.GetRequiredService<SprintPlanningTools>();
        var sprintRepository = scope.ServiceProvider.GetRequiredService<ISprintRepository>();

        var runnable = await jobs.GetRunnableAsync(cancellationToken);
        var job = runnable.FirstOrDefault();
        if (job is null)
        {
            return false;
        }

        if (job.CancelRequested)
        {
            job.Status = AiBatchJobStatus.Cancelled;
            job.StatusMessage = "Cancelled before start.";
            job.CompletedAt = DateTime.UtcNow;
            await jobs.UpdateAsync(job, cancellationToken);
            await notifier.NotifyAsync(SprintOptimizerJobMapper.ToDto(job), cancellationToken);
            return true;
        }

        job.Status = AiBatchJobStatus.Running;
        job.StartedAt ??= DateTime.UtcNow;
        job.HeartbeatAt = DateTime.UtcNow;
        job.CurrentPhase = SprintOptimizerPhase.Queued;
        job.StatusMessage = "Starting sprint optimizer…";
        await jobs.UpdateAsync(job, cancellationToken);
        await notifier.NotifyAsync(SprintOptimizerJobMapper.ToDto(job), cancellationToken);

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var heartbeatTask = HeartbeatLoopAsync(job.ExternalId, linkedCts.Token);

        try
        {
            var plan = await agent.PlanAsync(
                job.CreatedByUserId,
                job.MaxTasks,
                job.DurationDays,
                job.Name,
                linkedCts.Token,
                async (phase, message, ct) =>
                {
                    var latest = await jobs.GetByExternalIdAsync(job.ExternalId, ct);
                    if (latest is null)
                    {
                        return;
                    }

                    if (latest.CancelRequested)
                    {
                        linkedCts.Cancel();
                        throw new OperationCanceledException("Sprint optimizer job cancelled.");
                    }

                    latest.CurrentPhase = phase;
                    latest.StatusMessage = message;
                    latest.HeartbeatAt = DateTime.UtcNow;
                    latest.Status = AiBatchJobStatus.Running;
                    latest.ResultJson = SprintOptimizerJobMapper.SerializeProgressSteps(planningTools.Steps);
                    await jobs.UpdateAsync(latest, ct);
                    await notifier.NotifyAsync(SprintOptimizerJobMapper.ToDto(latest), ct);
                });

            var latestAfterPlan = await jobs.GetByExternalIdAsync(job.ExternalId, cancellationToken)
                                  ?? job;
            if (latestAfterPlan.CancelRequested)
            {
                latestAfterPlan.Status = AiBatchJobStatus.Cancelled;
                latestAfterPlan.StatusMessage = "Cancelled during planning.";
                latestAfterPlan.CompletedAt = DateTime.UtcNow;
                await jobs.UpdateAsync(latestAfterPlan, cancellationToken);
                await notifier.NotifyAsync(SprintOptimizerJobMapper.ToDto(latestAfterPlan), cancellationToken);
                return true;
            }

            latestAfterPlan.CurrentPhase = SprintOptimizerPhase.Persisting;
            latestAfterPlan.StatusMessage = "Persisting sprint result…";
            latestAfterPlan.HeartbeatAt = DateTime.UtcNow;
            latestAfterPlan.ResultJson = SprintOptimizerJobMapper.SerializeProgressSteps(planningTools.Steps);
            await jobs.UpdateAsync(latestAfterPlan, cancellationToken);
            await notifier.NotifyAsync(SprintOptimizerJobMapper.ToDto(latestAfterPlan), cancellationToken);

            var response = await BuildResponseAsync(latestAfterPlan, plan, sprintRepository, cancellationToken);

            latestAfterPlan.ResultJson = SprintOptimizerJobMapper.SerializeResult(response);
            latestAfterPlan.CreatedSprintId = response.SprintId;
            latestAfterPlan.Status = AiBatchJobStatus.Completed;
            latestAfterPlan.CurrentPhase = SprintOptimizerPhase.Done;
            latestAfterPlan.StatusMessage = $"Sprint created with {response.SelectedTasks.Count} tasks.";
            latestAfterPlan.CompletedAt = DateTime.UtcNow;
            latestAfterPlan.HeartbeatAt = DateTime.UtcNow;
            latestAfterPlan.LastError = null;
            await jobs.UpdateAsync(latestAfterPlan, cancellationToken);
            await notifier.NotifyAsync(SprintOptimizerJobMapper.ToDto(latestAfterPlan), cancellationToken);

            _logger.LogInformation(
                "Sprint optimizer job {JobId} completed. SprintId={SprintId}",
                latestAfterPlan.ExternalId,
                response.SprintId);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            var cancelled = await jobs.GetByExternalIdAsync(job.ExternalId, CancellationToken.None) ?? job;
            cancelled.Status = AiBatchJobStatus.Cancelled;
            cancelled.StatusMessage = "Cancelled.";
            cancelled.CompletedAt = DateTime.UtcNow;
            await jobs.UpdateAsync(cancelled, CancellationToken.None);
            await notifier.NotifyAsync(SprintOptimizerJobMapper.ToDto(cancelled), CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sprint optimizer job {JobId} failed", job.ExternalId);
            var failed = await jobs.GetByExternalIdAsync(job.ExternalId, CancellationToken.None) ?? job;
            failed.Status = AiBatchJobStatus.Failed;
            failed.LastError = Truncate(ex.Message, 2000);
            failed.StatusMessage = "Sprint optimizer failed.";
            failed.CompletedAt = DateTime.UtcNow;
            await jobs.UpdateAsync(failed, CancellationToken.None);
            await notifier.NotifyAsync(SprintOptimizerJobMapper.ToDto(failed), CancellationToken.None);
        }
        finally
        {
            linkedCts.Cancel();
            try
            {
                await heartbeatTask;
            }
            catch (OperationCanceledException)
            {
                // expected
            }
        }

        return true;
    }

    private async Task HeartbeatLoopAsync(Guid jobExternalId, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(15), cancellationToken);
            using var scope = _scopeFactory.CreateScope();
            var jobs = scope.ServiceProvider.GetRequiredService<ISprintOptimizerJobRepository>();
            var job = await jobs.GetByExternalIdAsync(jobExternalId, cancellationToken);
            if (job is null || job.Status != AiBatchJobStatus.Running)
            {
                return;
            }

            if (job.CancelRequested)
            {
                return;
            }

            job.HeartbeatAt = DateTime.UtcNow;
            await jobs.UpdateAsync(job, cancellationToken);
        }
    }

    private static async Task<OptimizeSprintResponseDto> BuildResponseAsync(
        SprintOptimizerJob job,
        SprintOptimizationPlan plan,
        ISprintRepository sprintRepository,
        CancellationToken cancellationToken)
    {
        Guid sprintId;
        string sprintName;
        DateTime startDate;
        DateTime endDate;

        if (plan.CreatedSprintId.HasValue
            && plan.CreatedStartDate.HasValue
            && plan.CreatedEndDate.HasValue)
        {
            sprintId = plan.CreatedSprintId.Value;
            sprintName = plan.CreatedSprintName ?? $"Optimized Sprint {plan.CreatedStartDate:yyyy-MM-dd}";
            startDate = plan.CreatedStartDate.Value;
            endDate = plan.CreatedEndDate.Value;
        }
        else
        {
            startDate = DateTime.UtcNow.Date;
            endDate = startDate.AddDays(job.DurationDays);
            sprintName = string.IsNullOrWhiteSpace(job.Name)
                ? $"Optimized Sprint {startDate:yyyy-MM-dd}"
                : job.Name.Trim();

            var sprint = new Sprint
            {
                Name = sprintName,
                StartDate = startDate,
                EndDate = endDate,
                CreatedByUserId = job.CreatedByUserId,
                CreatedAt = DateTime.UtcNow,
                Reasoning = plan.Reasoning
            };
            sprint.GenerateNewExternalId();

            foreach (var todo in plan.SelectedTodos)
            {
                sprint.SprintTodos.Add(new SprintTodo { TodoId = todo.Id });
            }

            await sprintRepository.Create(sprint, cancellationToken);
            sprintId = sprint.ExternalId;
        }

        return new OptimizeSprintResponseDto
        {
            SprintId = sprintId,
            Name = sprintName,
            StartDate = startDate,
            EndDate = endDate,
            Reasoning = plan.Reasoning,
            Steps = plan.Steps,
            SelectedTasks = plan.SelectedTodos.Select(t => new SprintTaskSummaryDto
            {
                ExternalId = t.ExternalId,
                Title = t.Title,
                Priority = t.Priority,
                Status = t.Status,
                DueDate = t.DueDate,
                Category = t.Category
            }).ToList()
        };
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
