#nullable enable

using System;
using System.Collections.Generic;
using Fistix.TaskManager.Core.DomainModel.Aggregates;
using Fistix.TaskManager.ViewModel.Commands.Todos;

namespace Fistix.TaskManager.ServiceLayer.Todos;

public sealed class SprintWorkloadStats
{
    public int TotalCandidates { get; set; }
    public int Overdue { get; set; }
    public int DueInSprintWindow { get; set; }
    public int ExcludedInActiveSprint { get; set; }
}

public sealed class AnalystOutput
{
    public List<Guid> RecommendedIds { get; set; } = [];
    public List<string> Risks { get; set; } = [];
    public string Theme { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public SprintWorkloadStats? Stats { get; set; }
}

public sealed class SprintOptimizerCheckpointDto
{
    public string CurrentPhase { get; set; } = string.Empty;
    public string? AnalystSummary { get; set; }
    public AnalystOutput? AnalystOutput { get; set; }
    public List<AgentStepDto> Steps { get; set; } = [];
    public int ToolInvocationCount { get; set; }
    public string? WorkflowCheckpointId { get; set; }
    public DateTime SavedAt { get; set; } = DateTime.UtcNow;
}

public sealed class SprintWorkflowRequest
{
    public Guid OwnerId { get; set; }
    public int MaxTasks { get; set; }
    public int DurationDays { get; set; }
    public string? Name { get; set; }
    public IReadOnlyList<TodoTask> Candidates { get; set; } = [];
    public SprintWorkloadStats Stats { get; set; } = new();
}
