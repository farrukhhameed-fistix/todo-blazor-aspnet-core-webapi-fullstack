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

public sealed class AiBatchJobRepository : IAiBatchJobRepository
{
    private readonly EfContext _context;

    public AiBatchJobRepository(EfContext context)
    {
        _context = context;
    }

    public async Task CreateAsync(AiBatchJob job, CancellationToken cancellationToken)
    {
        _context.AiBatchJobs.Add(job);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(AiBatchJob job, CancellationToken cancellationToken)
    {
        job.UpdatedAt = DateTime.UtcNow;
        _context.AiBatchJobs.Update(job);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public Task<AiBatchJob?> GetByExternalIdAsync(Guid externalId, CancellationToken cancellationToken) =>
        _context.AiBatchJobs.FirstOrDefaultAsync(j => j.ExternalId == externalId, cancellationToken);

    public Task<AiBatchJob?> GetActiveByOwnerAsync(Guid ownerExternalId, CancellationToken cancellationToken)
    {
        var active = new[]
        {
            AiBatchJobStatus.Pending,
            AiBatchJobStatus.Running,
            AiBatchJobStatus.Paused,
            AiBatchJobStatus.Stuck
        };

        return _context.AiBatchJobs
            .Where(j => j.CreatedByUserId == ownerExternalId && active.Contains(j.Status))
            .OrderByDescending(j => j.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AiBatchJob>> GetRunnableAsync(CancellationToken cancellationToken)
    {
        return await _context.AiBatchJobs
            .Where(j => j.Status == AiBatchJobStatus.Running || j.Status == AiBatchJobStatus.Pending)
            .OrderBy(j => j.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AiBatchJob>> GetStaleRunningAsync(TimeSpan staleAfter, CancellationToken cancellationToken)
    {
        var cutoff = DateTime.UtcNow - staleAfter;
        return await _context.AiBatchJobs
            .Where(j => j.Status == AiBatchJobStatus.Running
                        && j.HeartbeatAt != null
                        && j.HeartbeatAt < cutoff)
            .ToListAsync(cancellationToken);
    }
}
