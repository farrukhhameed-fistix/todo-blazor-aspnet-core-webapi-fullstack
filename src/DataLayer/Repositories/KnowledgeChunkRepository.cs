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

public sealed class KnowledgeChunkRepository : IKnowledgeChunkRepository
{
    private readonly EfContext _context;

    public KnowledgeChunkRepository(EfContext context)
    {
        _context = context;
    }

    public async Task ReplaceChunksAsync(
        int documentId,
        IReadOnlyList<KnowledgeChunk> chunks,
        CancellationToken cancellationToken)
    {
        var existing = await _context.KnowledgeChunks
            .Where(c => c.DocumentId == documentId)
            .ToListAsync(cancellationToken);
        _context.KnowledgeChunks.RemoveRange(existing);

        foreach (var chunk in chunks)
        {
            chunk.DocumentId = documentId;
            _context.KnowledgeChunks.Add(chunk);
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<KnowledgeChunk>> GetByDocumentIdAsync(
        int documentId,
        CancellationToken cancellationToken)
    {
        return await _context.KnowledgeChunks
            .AsNoTracking()
            .Where(c => c.DocumentId == documentId)
            .OrderBy(c => c.Ordinal)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<KnowledgeChunk>> GetByExternalIdsAsync(
        IReadOnlyCollection<Guid> externalIds,
        CancellationToken cancellationToken)
    {
        if (externalIds.Count == 0)
        {
            return Array.Empty<KnowledgeChunk>();
        }

        var ids = externalIds.ToList();
        return await _context.KnowledgeChunks
            .AsNoTracking()
            .Where(c => ids.Contains(c.ExternalId))
            .ToListAsync(cancellationToken);
    }
}
