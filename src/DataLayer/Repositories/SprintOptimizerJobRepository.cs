#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fistix.TaskManager.Core.Abstractions.Repositories;
using Fistix.TaskManager.Core.DomainModel.Aggregates;
using Fistix.TaskManager.Core.DomainModel.Constants;
using Microsoft.EntityFrameworkCore;

namespace Fistix.TaskManager.DataLayer.Repositories;

public sealed class SprintOptimizerJobRepository : ISprintOptimizerJobRepository
{
    private readonly EfContext _context;

    public SprintOptimizerJobRepository(EfContext context)
    {
        _context = context;
    }

    public async Task CreateAsync(SprintOptimizerJob job, CancellationToken cancellationToken)
    {
        _context.SprintOptimizerJobs.Add(job);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(SprintOptimizerJob job, CancellationToken cancellationToken)
    {
        job.UpdatedAt = DateTime.UtcNow;
        _context.SprintOptimizerJobs.Update(job);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public Task<SprintOptimizerJob?> GetByExternalIdAsync(Guid externalId, CancellationToken cancellationToken) =>
        _context.SprintOptimizerJobs.FirstOrDefaultAsync(j => j.ExternalId == externalId, cancellationToken);

    public Task<SprintOptimizerJob?> GetActiveByOwnerAsync(Guid ownerExternalId, CancellationToken cancellationToken)
    {
        var active = new[]
        {
            AiBatchJobStatus.Pending,
            AiBatchJobStatus.Running,
            AiBatchJobStatus.Stuck,
            AiBatchJobStatus.AwaitingApproval
        };

        return _context.SprintOptimizerJobs
            .Where(j => j.CreatedByUserId == ownerExternalId && active.Contains(j.Status))
            .OrderByDescending(j => j.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SprintOptimizerJob>> GetRunnableAsync(CancellationToken cancellationToken)
    {
        var runnable = new[]
        {
            AiBatchJobStatus.Pending,
            AiBatchJobStatus.Stuck
        };

        return await _context.SprintOptimizerJobs
            .Where(j => runnable.Contains(j.Status))
            .OrderBy(j => j.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SprintOptimizerJob>> GetStaleRunningAsync(
        TimeSpan staleAfter,
        CancellationToken cancellationToken)
    {
        var cutoff = DateTime.UtcNow - staleAfter;
        return await _context.SprintOptimizerJobs
            .Where(j => j.Status == AiBatchJobStatus.Running
                        && j.HeartbeatAt != null
                        && j.HeartbeatAt < cutoff)
            .ToListAsync(cancellationToken);
    }
}
