#nullable enable

using System;
using System.Collections.Generic;
using Fistix.TaskManager.Core.DomainModel.Constants;
using Fistix.TaskManager.Core.DomainModel.SeedWork;

namespace Fistix.TaskManager.Core.DomainModel.Aggregates;

public class KnowledgeDocument : Entity
{
    public Guid CreatedByUserId { get; set; }

    public string FileName { get; set; } = string.Empty;

    public string ContentType { get; set; } = "text/plain";

    public long FileSizeBytes { get; set; }

    public string Status { get; set; } = KnowledgeDocumentStatus.Pending;

    public string? ExtractedText { get; set; }

    public int ChunkCount { get; set; }

    public string? Error { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public virtual ICollection<KnowledgeChunk> Chunks { get; set; } = new List<KnowledgeChunk>();

    public virtual ICollection<KnowledgeIngestJob> Jobs { get; set; } = new List<KnowledgeIngestJob>();
}
