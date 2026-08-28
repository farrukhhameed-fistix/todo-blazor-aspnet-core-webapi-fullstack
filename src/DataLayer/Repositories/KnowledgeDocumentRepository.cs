#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fistix.TaskManager.Core.Abstractions.Repositories;
using Fistix.TaskManager.Core.DomainModel.Aggregates;
using Microsoft.EntityFrameworkCore;

namespace Fistix.TaskManager.DataLayer.Repositories;

public sealed class KnowledgeDocumentRepository : IKnowledgeDocumentRepository
{
    private readonly EfContext _context;

    public KnowledgeDocumentRepository(EfContext context)
    {
        _context = context;
    }

    public async Task CreateAsync(KnowledgeDocument document, CancellationToken cancellationToken)
    {
        _context.KnowledgeDocuments.Add(document);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(KnowledgeDocument document, CancellationToken cancellationToken)
    {
        document.UpdatedAt = DateTime.UtcNow;
        _context.KnowledgeDocuments.Update(document);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public Task<KnowledgeDocument?> GetByExternalIdAsync(Guid externalId, CancellationToken cancellationToken) =>
        _context.KnowledgeDocuments.FirstOrDefaultAsync(d => d.ExternalId == externalId, cancellationToken);

    public Task<KnowledgeDocument?> GetByIdAsync(int id, CancellationToken cancellationToken) =>
        _context.KnowledgeDocuments.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

    public async Task<IReadOnlyList<KnowledgeDocument>> ListByOwnerAsync(
        Guid ownerExternalId,
        CancellationToken cancellationToken)
    {
        return await _context.KnowledgeDocuments
            .AsNoTracking()
            .Where(d => d.CreatedByUserId == ownerExternalId)
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task DeleteAsync(KnowledgeDocument document, CancellationToken cancellationToken)
    {
        _context.KnowledgeDocuments.Remove(document);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
