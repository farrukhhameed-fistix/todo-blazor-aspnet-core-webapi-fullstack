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

public sealed class KnowledgeIngestJobRepository : IKnowledgeIngestJobRepository
{
    private readonly EfContext _context;

    public KnowledgeIngestJobRepository(EfContext context)
    {
        _context = context;
    }

    public async Task CreateAsync(KnowledgeIngestJob job, CancellationToken cancellationToken)
    {
        _context.KnowledgeIngestJobs.Add(job);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(KnowledgeIngestJob job, CancellationToken cancellationToken)
    {
        job.UpdatedAt = DateTime.UtcNow;
        _context.KnowledgeIngestJobs.Update(job);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public Task<KnowledgeIngestJob?> GetByExternalIdAsync(Guid externalId, CancellationToken cancellationToken) =>
        _context.KnowledgeIngestJobs.FirstOrDefaultAsync(j => j.ExternalId == externalId, cancellationToken);

    public Task<KnowledgeIngestJob?> GetLatestByDocumentIdAsync(int documentId, CancellationToken cancellationToken) =>
        _context.KnowledgeIngestJobs
            .Where(j => j.DocumentId == documentId)
            .OrderByDescending(j => j.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<KnowledgeIngestJob>> GetRunnableAsync(CancellationToken cancellationToken)
    {
        return await _context.KnowledgeIngestJobs
            .Where(j => j.Status == AiBatchJobStatus.Running || j.Status == AiBatchJobStatus.Pending)
            .OrderBy(j => j.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<KnowledgeIngestJob>> GetStaleRunningAsync(
        TimeSpan staleAfter,
        CancellationToken cancellationToken)
    {
        var cutoff = DateTime.UtcNow - staleAfter;
        return await _context.KnowledgeIngestJobs
            .Where(j => j.Status == AiBatchJobStatus.Running
                        && j.HeartbeatAt != null
                        && j.HeartbeatAt < cutoff)
            .ToListAsync(cancellationToken);
    }
}
