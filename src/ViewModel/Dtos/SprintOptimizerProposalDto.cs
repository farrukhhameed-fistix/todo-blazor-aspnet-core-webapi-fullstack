#nullable enable

using System;
using System.Collections.Generic;
using Fistix.TaskManager.ViewModel.Commands.Todos;

namespace Fistix.TaskManager.ViewModel.Dtos;

/// <summary>Proposed sprint plan awaiting user approval (not yet persisted).</summary>
public class SprintOptimizerProposalDto
{
    public List<SprintTaskSummaryDto> SelectedTasks { get; set; } = [];
    public string Reasoning { get; set; } = string.Empty;
    public List<AgentStepDto> Steps { get; set; } = [];
    public List<string> Risks { get; set; } = [];
    public string? Theme { get; set; }
    public bool UsedHeuristicFallback { get; set; }
    public DateTime ProposedAt { get; set; } = DateTime.UtcNow;
}
