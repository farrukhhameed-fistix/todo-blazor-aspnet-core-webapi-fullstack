#nullable enable

using System;
using Pgvector;

namespace Fistix.TaskManager.Core.DomainModel.Aggregates;

public class KnowledgeChunkEmbedding
{
    public int Id { get; set; }

    public int ChunkId { get; set; }

    public Vector Embedding { get; set; } = null!;

    public string EmbeddingModel { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }

    public virtual KnowledgeChunk? Chunk { get; set; }
}
