#nullable enable

using System;
using Fistix.TaskManager.Core.DomainModel.Constants;
using Fistix.TaskManager.Core.DomainModel.SeedWork;

namespace Fistix.TaskManager.Core.DomainModel.Aggregates;

public class KnowledgeIngestJob : Entity
{
    public int DocumentId { get; set; }

    public Guid CreatedByUserId { get; set; }

    public string CurrentStep { get; set; } = KnowledgeIngestStepNames.Parse;

    public string Status { get; set; } = AiBatchJobStatus.Pending;

    public string? LastError { get; set; }

    public int ChunksEmbedded { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? HeartbeatAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    public virtual KnowledgeDocument? Document { get; set; }
}
