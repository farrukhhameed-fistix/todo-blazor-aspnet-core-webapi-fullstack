#nullable enable

using System;
using System.Collections.Generic;
using Fistix.TaskManager.Core.DomainModel.SeedWork;

namespace Fistix.TaskManager.Core.DomainModel.Aggregates;

public class KnowledgeChunk : Entity
{
    public int DocumentId { get; set; }

    public int Ordinal { get; set; }

    public string Content { get; set; } = string.Empty;

    public string? Heading { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual KnowledgeDocument? Document { get; set; }

    public virtual ICollection<KnowledgeChunkEmbedding> Embeddings { get; set; } = new List<KnowledgeChunkEmbedding>();
}
