#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Fistix.TaskManager.Core.DomainModel.Aggregates;
using Fistix.TaskManager.Core.DomainModel.Constants;
using Fistix.TaskManager.ViewModel.Commands.Todos;

namespace Fistix.TaskManager.ServiceLayer.Todos;

/// <summary>Persists and restores sprint optimizer worker checkpoints for resume after crash/stuck.</summary>
public sealed class SprintOptimizerCheckpointService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public SprintOptimizerCheckpointDto Create(
        string currentPhase,
        string? analystSummary,
        AnalystOutput? analystOutput,
        IReadOnlyList<AgentStepDto> steps,
        int toolInvocationCount,
        string? workflowCheckpointId = null) =>
        new()
        {
            CurrentPhase = currentPhase,
            AnalystSummary = analystSummary,
            AnalystOutput = analystOutput,
            Steps = steps.ToList(),
            ToolInvocationCount = toolInvocationCount,
            WorkflowCheckpointId = workflowCheckpointId,
            SavedAt = DateTime.UtcNow
        };

    public void ApplyToJob(SprintOptimizerJob job, SprintOptimizerCheckpointDto checkpoint)
    {
        job.CheckpointJson = Serialize(checkpoint);
        job.CurrentPhase = checkpoint.CurrentPhase;
        if (!string.IsNullOrWhiteSpace(checkpoint.WorkflowCheckpointId))
        {
            job.PendingRequestId = checkpoint.WorkflowCheckpointId;
        }
    }

    public SprintOptimizerCheckpointDto? Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<SprintOptimizerCheckpointDto>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public string Serialize(SprintOptimizerCheckpointDto checkpoint) =>
        JsonSerializer.Serialize(checkpoint, JsonOptions);

    public bool CanResumeFromPlanner(SprintOptimizerCheckpointDto? checkpoint) =>
        checkpoint?.AnalystOutput is not null
        && (string.Equals(checkpoint.CurrentPhase, SprintOptimizerPhase.Planner, StringComparison.OrdinalIgnoreCase)
            || string.Equals(checkpoint.CurrentPhase, SprintOptimizerPhase.Analyst, StringComparison.OrdinalIgnoreCase));
}
