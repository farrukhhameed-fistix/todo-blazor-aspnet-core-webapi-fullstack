#nullable enable

using Fistix.TaskManager.Core.DomainModel.Aggregates;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Fistix.TaskManager.Core.Abstractions.Repositories;

public interface ITodoEmbeddingRepository
{
    Task UpsertAsync(int todoId, float[] embedding, string model, CancellationToken cancellationToken);
    Task<TodoEmbedding?> GetByTodoIdAsync(int todoId, CancellationToken cancellationToken);
    Task<IReadOnlyList<TodoEmbeddingSearchHit>> SearchSimilarAsync(
        float[] queryEmbedding,
        string embeddingModel,
        Guid? ownerExternalId,
        int limit,
        CancellationToken cancellationToken,
        IReadOnlyCollection<Guid>? allowedExternalIds = null);
    Task<IReadOnlyList<Guid>> GetTodoExternalIdsMissingEmbeddingsAsync(
        string embeddingModel,
        CancellationToken cancellationToken);
}

public sealed record TodoEmbeddingSearchHit(Guid TodoExternalId, int TodoId, double Distance);
