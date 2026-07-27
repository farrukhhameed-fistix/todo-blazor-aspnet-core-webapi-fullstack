#nullable enable

using System;
using Fistix.TaskManager.Core.DomainModel.Constants;
using Fistix.TaskManager.Core.DomainModel.SeedWork;

namespace Fistix.TaskManager.Core.DomainModel.Aggregates;

/// <summary>
/// Durable, pauseable AI processing job over an ordered list of todo external ids.
/// </summary>
public class AiBatchJob : Entity
{
    public Guid CreatedByUserId { get; set; }

    /// <summary>Comma-separated ordered steps, e.g. Embedding,Classify,Summarize.</summary>
    public string StepsCsv { get; set; } = string.Join(',', AiBatchStepNames.DefaultSteps);

    public string CurrentStep { get; set; } = AiBatchStepNames.Embedding;

    /// <summary>JSON array of todo ExternalId values.</summary>
    public string TodoExternalIdsJson { get; set; } = "[]";

    public int Cursor { get; set; }

    public int Total { get; set; }

    public int Completed { get; set; }

    public int Failed { get; set; }

    public int Skipped { get; set; }

    public int BatchSize { get; set; } = 5;

    public int DelayMsBetweenItems { get; set; }

    public bool OnlyMissing { get; set; } = true;

    public string Status { get; set; } = AiBatchJobStatus.Pending;

    public bool CancelRequested { get; set; }

    public string? ImportTag { get; set; }

    public string? LastError { get; set; }

    public Guid? LastTodoExternalId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? HeartbeatAt { get; set; }

    public DateTime? PausedAt { get; set; }

    public DateTime? CompletedAt { get; set; }
}
