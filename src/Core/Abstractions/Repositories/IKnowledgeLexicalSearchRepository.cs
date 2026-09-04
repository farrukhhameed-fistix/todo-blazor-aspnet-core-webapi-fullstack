#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Fistix.TaskManager.Core.Abstractions.Repositories;

public interface IKnowledgeLexicalSearchRepository
{
    Task<IReadOnlyList<KnowledgeLexicalSearchHit>> SearchAsync(
        string query,
        Guid ownerExternalId,
        int limit,
        CancellationToken cancellationToken,
        Guid? documentExternalId = null,
        IReadOnlyCollection<Guid>? excludeChunkExternalIds = null);
}

public sealed record KnowledgeLexicalSearchHit(
    Guid ChunkExternalId,
    int ChunkId,
    Guid DocumentExternalId,
    string FileName,
    int Ordinal,
    string Content,
    string? Heading,
    double Rank);
