#nullable enable

using System;
using System.Collections.Generic;
using Fistix.TaskManager.ViewModel.Commands.Todos;

namespace Fistix.TaskManager.ViewModel.Dtos;

public class SprintOptimizerJobDto
{
    public Guid ExternalId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string CurrentPhase { get; set; } = string.Empty;
    public string? StatusMessage { get; set; }
    public int MaxTasks { get; set; }
    public int DurationDays { get; set; }
    public string? Name { get; set; }
    public string? LastError { get; set; }
    public Guid? CreatedSprintId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? HeartbeatAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public OptimizeSprintResponseDto? Result { get; set; }
    public SprintOptimizerProposalDto? Proposal { get; set; }
}
