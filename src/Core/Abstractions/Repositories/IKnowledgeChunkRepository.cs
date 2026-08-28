#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fistix.TaskManager.Core.DomainModel.Aggregates;

namespace Fistix.TaskManager.Core.Abstractions.Repositories;

public interface IKnowledgeChunkRepository
{
    Task ReplaceChunksAsync(int documentId, IReadOnlyList<KnowledgeChunk> chunks, CancellationToken cancellationToken);

    Task<IReadOnlyList<KnowledgeChunk>> GetByDocumentIdAsync(int documentId, CancellationToken cancellationToken);

    Task<IReadOnlyList<KnowledgeChunk>> GetByExternalIdsAsync(
        IReadOnlyCollection<Guid> externalIds,
        CancellationToken cancellationToken);
}
