#nullable enable

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fistix.TaskManager.AiLayer.Shared;
using Fistix.TaskManager.Core.Abstractions.Repositories;
using Fistix.TaskManager.Core.DomainModel.Constants;
using Fistix.TaskManager.ServiceLayer.Knowledge;
using Fistix.TaskManager.ServiceLayer.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Fistix.TaskManager.ServiceLayer.Background;

public sealed class KnowledgeIngestBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AiConfiguration _aiConfig;
    private readonly ILogger<KnowledgeIngestBackgroundService> _logger;

    public KnowledgeIngestBackgroundService(
        IServiceScopeFactory scopeFactory,
        AiConfiguration aiConfig,
        ILogger<KnowledgeIngestBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _aiConfig = aiConfig;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Knowledge ingest background service started");

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
                _logger.LogError(ex, "Knowledge ingest worker loop error");
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
        }
    }

    private async Task MarkStuckJobsAsync(CancellationToken cancellationToken)
    {
        var stuckAfterSeconds = Math.Max(30, _aiConfig.Features.KnowledgeRag.StuckAfterSeconds);
        using var scope = _scopeFactory.CreateScope();
        var jobs = scope.ServiceProvider.GetRequiredService<IKnowledgeIngestJobRepository>();
        var documents = scope.ServiceProvider.GetRequiredService<IKnowledgeDocumentRepository>();
        var notifier = scope.ServiceProvider.GetRequiredService<IKnowledgeIngestNotifier>();
        var stale = await jobs.GetStaleRunningAsync(TimeSpan.FromSeconds(stuckAfterSeconds), cancellationToken);
        foreach (var job in stale)
        {
            job.Status = AiBatchJobStatus.Stuck;
            job.LastError = $"No heartbeat for more than {stuckAfterSeconds}s.";
            await jobs.UpdateAsync(job, cancellationToken);

            var document = await documents.GetByIdAsync(job.DocumentId, cancellationToken);
            if (document is not null)
            {
                document.Status = KnowledgeDocumentStatus.Failed;
                document.Error = job.LastError;
                await documents.UpdateAsync(document, cancellationToken);
                await notifier.NotifyAsync(KnowledgeMapper.ToJobDto(job, document), cancellationToken);
            }

            _logger.LogWarning("Marked knowledge ingest job {JobId} as Stuck", job.ExternalId);
        }
    }

    private async Task<bool> ProcessOneTickAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var jobs = scope.ServiceProvider.GetRequiredService<IKnowledgeIngestJobRepository>();
        var processor = scope.ServiceProvider.GetRequiredService<IKnowledgeIngestProcessor>();

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

        job = await jobs.GetByExternalIdAsync(job.ExternalId, cancellationToken);
        if (job is null || job.Status != AiBatchJobStatus.Running)
        {
            return false;
        }

        await processor.ProcessNextStepAsync(job, cancellationToken);
        return true;
    }
}
