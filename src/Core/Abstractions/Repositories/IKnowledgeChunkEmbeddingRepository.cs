#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Fistix.TaskManager.Core.Abstractions.Repositories;

public interface IKnowledgeChunkEmbeddingRepository
{
    Task UpsertAsync(int chunkId, float[] embedding, string model, CancellationToken cancellationToken);

    Task<IReadOnlyList<KnowledgeChunkSearchHit>> SearchSimilarAsync(
        float[] queryEmbedding,
        string embeddingModel,
        Guid ownerExternalId,
        int limit,
        CancellationToken cancellationToken,
        Guid? documentExternalId = null);
}

public sealed record KnowledgeChunkSearchHit(
    Guid ChunkExternalId,
    int ChunkId,
    Guid DocumentExternalId,
    string FileName,
    int Ordinal,
    string Content,
    string? Heading,
    double Distance);
