#nullable enable

using System;
using System.Collections.Generic;
using Fistix.TaskManager.ViewModel.Dtos;
using MediatR;

namespace Fistix.TaskManager.ViewModel.Commands.Todos;

public class OptimizeSprintCommand : IRequest<OptimizeSprintCommandResult>
{
    public int MaxTasks { get; set; } = 12;
    public int DurationDays { get; set; } = 14;
    public string? Name { get; set; }
}

/// <summary>Starts an async sprint optimizer job; result is delivered via get/active + SignalR.</summary>
public class OptimizeSprintCommandResult
{
    public SprintOptimizerJobDto Payload { get; set; } = new();
}

public class CancelSprintOptimizerJobCommand : IRequest<CancelSprintOptimizerJobCommandResult>
{
    public Guid JobExternalId { get; set; }
}

public class CancelSprintOptimizerJobCommandResult
{
    public SprintOptimizerJobDto Payload { get; set; } = new();
}

public class ApproveSprintOptimizerProposalCommand : IRequest<ApproveSprintOptimizerProposalCommandResult>
{
    public Guid JobExternalId { get; set; }

    /// <summary>Optional edited list of todo external ids. When empty, uses the stored proposal.</summary>
    public List<Guid> SelectedTaskExternalIds { get; set; } = [];
}

public class ApproveSprintOptimizerProposalCommandResult
{
    public SprintOptimizerJobDto Payload { get; set; } = new();
}

public class RejectSprintOptimizerProposalCommand : IRequest<RejectSprintOptimizerProposalCommandResult>
{
    public Guid JobExternalId { get; set; }
}

public class RejectSprintOptimizerProposalCommandResult
{
    public SprintOptimizerJobDto Payload { get; set; } = new();
}

public class OptimizeSprintResponseDto
{
    public Guid SprintId { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public List<SprintTaskSummaryDto> SelectedTasks { get; set; } = new();
    public string Reasoning { get; set; } = string.Empty;
    /// <summary>Ordered tool invocations from the Microsoft Agent Framework run (demo trail).</summary>
    public List<AgentStepDto> Steps { get; set; } = new();
}

public class AgentStepDto
{
    /// <summary>Analyst, Planner, SprintAgent, or heuristic_fallback.</summary>
    public string AgentName { get; set; } = string.Empty;
    public string ToolName { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
}
