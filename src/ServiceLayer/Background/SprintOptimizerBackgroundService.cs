#nullable enable

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fistix.TaskManager.AiLayer.Observability;
using Fistix.TaskManager.AiLayer.Shared;
using Fistix.TaskManager.Core.Abstractions.Repositories;
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
/// Planning stops at AwaitingApproval; sprint persist requires explicit user approval.
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
            job.LastError = $"No heartbeat for more than {stuckAfterSeconds}s. Worker will resume from checkpoint.";
            job.StatusMessage = "Marked stuck (no heartbeat); resume pending.";
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
        var persistService = scope.ServiceProvider.GetRequiredService<SprintOptimizerPersistService>();
        var checkpointService = scope.ServiceProvider.GetRequiredService<SprintOptimizerCheckpointService>();
        var telemetry = scope.ServiceProvider.GetService<IAiTelemetry>() ?? NullAiTelemetry.Instance;

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

        var resuming = string.Equals(job.Status, AiBatchJobStatus.Stuck, StringComparison.OrdinalIgnoreCase);
        var resumeCheckpoint = resuming ? checkpointService.Deserialize(job.CheckpointJson) : null;

        job.Status = AiBatchJobStatus.Running;
        job.StartedAt ??= DateTime.UtcNow;
        job.HeartbeatAt = DateTime.UtcNow;
        job.CurrentPhase = resuming && resumeCheckpoint is not null
            ? resumeCheckpoint.CurrentPhase
            : SprintOptimizerPhase.Queued;
        job.StatusMessage = resuming
            ? "Resuming sprint optimizer from checkpoint…"
            : "Starting sprint optimizer…";
        job.LastError = resuming ? null : job.LastError;
        await jobs.UpdateAsync(job, cancellationToken);
        await notifier.NotifyAsync(SprintOptimizerJobMapper.ToDto(job), cancellationToken);

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var jobTimeoutSeconds = Math.Max(30, _aiConfig.Agents?.JobTimeoutSeconds ?? 240);
        linkedCts.CancelAfter(TimeSpan.FromSeconds(jobTimeoutSeconds));
        var heartbeatTask = HeartbeatLoopAsync(job.ExternalId, linkedCts.Token);
        using var operation = telemetry.StartOperation(
            AiTelemetryNames.Features.SprintOptimizer,
            provider: _aiConfig.Provider,
            jobExternalId: job.ExternalId);
        operation.Activity?.SetTag(AiTelemetryNames.Tags.PromptVersion, AiPromptVersions.SprintOptimizer);

        try
        {
            var plan = await agent.PlanAsync(
                job.CreatedByUserId,
                job.MaxTasks,
                job.DurationDays,
                job.Name,
                linkedCts.Token,
                async (phase, message, analystOutput, ct) =>
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

                    var checkpoint = checkpointService.Create(
                        phase,
                        analystOutput?.Summary,
                        analystOutput,
                        planningTools.Steps,
                        planningTools.ToolInvocationCount);
                    checkpointService.ApplyToJob(latest, checkpoint);

                    await jobs.UpdateAsync(latest, ct);
                    await notifier.NotifyAsync(SprintOptimizerJobMapper.ToDto(latest), ct);
                },
                resumeCheckpoint);

            var latestAfterPlan = await jobs.GetByExternalIdAsync(job.ExternalId, cancellationToken)
                                  ?? job;
            if (latestAfterPlan.CancelRequested)
            {
                latestAfterPlan.Status = AiBatchJobStatus.Cancelled;
                latestAfterPlan.StatusMessage = "Cancelled during planning.";
                latestAfterPlan.CompletedAt = DateTime.UtcNow;
                await jobs.UpdateAsync(latestAfterPlan, cancellationToken);
                await notifier.NotifyAsync(SprintOptimizerJobMapper.ToDto(latestAfterPlan), cancellationToken);
                operation.SetOutcome(AiTelemetryNames.Outcomes.Cancelled);
                return true;
            }

            if (plan.SelectedTodos.Count == 0)
            {
                latestAfterPlan.Status = AiBatchJobStatus.Failed;
                latestAfterPlan.CurrentPhase = SprintOptimizerPhase.Done;
                latestAfterPlan.StatusMessage = "No tasks could be proposed for this sprint.";
                latestAfterPlan.LastError = "Planner and heuristic fallback produced an empty selection.";
                latestAfterPlan.CompletedAt = DateTime.UtcNow;
                await jobs.UpdateAsync(latestAfterPlan, cancellationToken);
                await notifier.NotifyAsync(SprintOptimizerJobMapper.ToDto(latestAfterPlan), cancellationToken);
                operation.SetOutcome(AiTelemetryNames.Outcomes.ValidationFailed);
                return true;
            }

            var usedHeuristic = plan.Steps.Any(s =>
                string.Equals(s.ToolName, "heuristic_fallback", StringComparison.OrdinalIgnoreCase));
            var proposal = persistService.BuildProposal(plan, usedHeuristic);

            latestAfterPlan.Status = AiBatchJobStatus.AwaitingApproval;
            latestAfterPlan.CurrentPhase = SprintOptimizerPhase.AwaitingApproval;
            latestAfterPlan.StatusMessage =
                $"Review proposed sprint ({proposal.SelectedTasks.Count} tasks) and approve or reject.";
            latestAfterPlan.ProposalJson = SprintOptimizerJobMapper.SerializeProposal(proposal);
            latestAfterPlan.ResultJson = null;
            latestAfterPlan.CheckpointJson = null;
            latestAfterPlan.PendingRequestId = null;
            latestAfterPlan.HeartbeatAt = DateTime.UtcNow;
            latestAfterPlan.LastError = null;
            await jobs.UpdateAsync(latestAfterPlan, cancellationToken);
            await notifier.NotifyAsync(SprintOptimizerJobMapper.ToDto(latestAfterPlan), cancellationToken);
            operation.SetOutcome(AiTelemetryNames.Outcomes.Success);

            _logger.LogInformation(
                "Sprint optimizer job {JobId} awaiting approval with {TaskCount} proposed tasks",
                latestAfterPlan.ExternalId,
                proposal.SelectedTasks.Count);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            operation.SetOutcome(AiTelemetryNames.Outcomes.Cancelled);
            throw;
        }
        catch (OperationCanceledException)
        {
            operation.SetOutcome(AiTelemetryNames.Outcomes.BudgetExceeded);
            telemetry.RecordQualityEvent(
                AiTelemetryNames.Features.SprintOptimizer,
                AiTelemetryNames.QualityEvents.BudgetExceeded);
            var cancelled = await jobs.GetByExternalIdAsync(job.ExternalId, CancellationToken.None) ?? job;
            cancelled.Status = AiBatchJobStatus.Cancelled;
            cancelled.StatusMessage = "Cancelled or timed out (job budget).";
            cancelled.CompletedAt = DateTime.UtcNow;
            await jobs.UpdateAsync(cancelled, CancellationToken.None);
            await notifier.NotifyAsync(SprintOptimizerJobMapper.ToDto(cancelled), CancellationToken.None);
        }
        catch (Exception ex)
        {
            operation.SetOutcome(AiTelemetryNames.Outcomes.Error);
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

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
