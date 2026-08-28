#nullable enable

using System;
using Fistix.TaskManager.Core.DomainModel.Constants;
using Fistix.TaskManager.Core.DomainModel.SeedWork;

namespace Fistix.TaskManager.Core.DomainModel.Aggregates;

/// <summary>
/// Durable async sprint optimizer run (Analyst → Planner) with SignalR progress.
/// </summary>
public class SprintOptimizerJob : Entity
{
    public Guid CreatedByUserId { get; set; }

    public int MaxTasks { get; set; } = 12;

    public int DurationDays { get; set; } = 14;

    public string? Name { get; set; }

    public string Status { get; set; } = AiBatchJobStatus.Pending;

    public string CurrentPhase { get; set; } = SprintOptimizerPhase.Queued;

    public string? StatusMessage { get; set; }

    public bool CancelRequested { get; set; }

    public string? LastError { get; set; }

    /// <summary>JSON of SprintOptimizerProposalDto while awaiting approval.</summary>
    public string? ProposalJson { get; set; }

    /// <summary>JSON of SprintOptimizerCheckpointDto for worker resume.</summary>
    public string? CheckpointJson { get; set; }

    /// <summary>MAF workflow checkpoint id when using WorkflowBuilder resume.</summary>
    public string? PendingRequestId { get; set; }

    /// <summary>JSON of OptimizeSprintResponseDto when completed.</summary>
    public string? ResultJson { get; set; }

    public Guid? CreatedSprintId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? HeartbeatAt { get; set; }

    public DateTime? StartedAt { get; set; }

    public DateTime? CompletedAt { get; set; }
}
