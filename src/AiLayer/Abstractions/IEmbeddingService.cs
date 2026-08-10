#nullable enable

namespace Fistix.TaskManager.AiLayer.Abstractions;

public enum EmbeddingInputKind
{
    /// <summary>Document/passage text (todo title + description).</summary>
    Passage = 0,
    /// <summary>Search query; ONNX BGE may prepend a retrieval instruction.</summary>
    Query = 1
}

public interface IEmbeddingService
{
    string ModelName { get; }
    int Dimension { get; }

    Task<float[]> GenerateEmbeddingAsync(
        string text,
        EmbeddingInputKind kind = EmbeddingInputKind.Passage,
        CancellationToken cancellationToken = default);
}

public interface IVectorStore
{
    Task UpsertTodoEmbeddingAsync(int todoId, float[] embedding, string model, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<VectorSearchHit>> SearchAsync(
        float[] queryEmbedding,
        string embeddingModel,
        Guid? ownerExternalId,
        int limit,
        CancellationToken cancellationToken = default,
        IReadOnlyCollection<Guid>? allowedExternalIds = null);
}

/// <summary>Postgres full-text search over todo title/description.</summary>
public interface ILexicalTodoSearch
{
    Task<IReadOnlyList<LexicalSearchHit>> SearchAsync(
        string query,
        Guid? ownerExternalId,
        int limit,
        IReadOnlyCollection<Guid>? allowedExternalIds = null,
        CancellationToken cancellationToken = default);
}

public sealed record VectorSearchHit(Guid TodoExternalId, int TodoId, double Similarity);

public sealed record LexicalSearchHit(Guid TodoExternalId, int TodoId, double Rank);
