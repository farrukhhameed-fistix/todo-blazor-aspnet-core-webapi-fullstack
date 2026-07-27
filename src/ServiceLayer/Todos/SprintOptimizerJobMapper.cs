#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Fistix.TaskManager.Core.DomainModel.Aggregates;
using Fistix.TaskManager.Core.DomainModel.Constants;
using Fistix.TaskManager.ViewModel.Commands.Todos;
using Fistix.TaskManager.ViewModel.Dtos;

namespace Fistix.TaskManager.ServiceLayer.Todos;

public static class SprintOptimizerJobMapper
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static SprintOptimizerJobDto ToDto(SprintOptimizerJob job)
    {
        OptimizeSprintResponseDto? result = null;
        if (!string.IsNullOrWhiteSpace(job.ResultJson))
        {
            try
            {
                result = JsonSerializer.Deserialize<OptimizeSprintResponseDto>(job.ResultJson, JsonOptions);
            }
            catch (JsonException)
            {
                result = null;
            }
        }

        return new SprintOptimizerJobDto
        {
            ExternalId = job.ExternalId,
            Status = job.Status,
            CurrentPhase = job.CurrentPhase,
            StatusMessage = job.StatusMessage,
            MaxTasks = job.MaxTasks,
            DurationDays = job.DurationDays,
            Name = job.Name,
            LastError = job.LastError,
            CreatedSprintId = job.CreatedSprintId,
            CreatedAt = job.CreatedAt,
            UpdatedAt = job.UpdatedAt,
            StartedAt = job.StartedAt,
            HeartbeatAt = job.HeartbeatAt,
            CompletedAt = job.CompletedAt,
            Result = result
        };
    }

    public static string SerializeResult(OptimizeSprintResponseDto result) =>
        JsonSerializer.Serialize(result, JsonOptions);

    public static string SerializeProgressSteps(IReadOnlyCollection<AgentStepDto> steps) =>
        JsonSerializer.Serialize(
            new OptimizeSprintResponseDto
            {
                Reasoning = string.Empty,
                Steps = steps.ToList()
            },
            JsonOptions);
}
