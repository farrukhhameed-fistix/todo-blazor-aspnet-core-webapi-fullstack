#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fistix.TaskManager.AiLayer.Abstractions;
using Fistix.TaskManager.Core.Abstractions.Repositories;

namespace Fistix.TaskManager.ServiceLayer.Todos;

public sealed class PgVectorEmbeddingStore : IVectorStore
{
    private readonly ITodoEmbeddingRepository _repository;

    public PgVectorEmbeddingStore(ITodoEmbeddingRepository repository)
    {
        _repository = repository;
    }

    public Task UpsertTodoEmbeddingAsync(
        int todoId,
        float[] embedding,
        string model,
        CancellationToken cancellationToken = default)
    {
        return _repository.UpsertAsync(todoId, embedding, model, cancellationToken);
    }

    public async Task<IReadOnlyList<VectorSearchHit>> SearchAsync(
        float[] queryEmbedding,
        string embeddingModel,
        Guid? ownerExternalId,
        int limit,
        CancellationToken cancellationToken = default,
        IReadOnlyCollection<Guid>? allowedExternalIds = null)
    {
        var hits = await _repository.SearchSimilarAsync(
            queryEmbedding,
            embeddingModel,
            ownerExternalId,
            limit,
            cancellationToken,
            allowedExternalIds);

        return hits
            .Select(h => new VectorSearchHit(
                h.TodoExternalId,
                h.TodoId,
                Similarity: Math.Max(0, 1.0 - h.Distance)))
            .ToList();
    }
}
