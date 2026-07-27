#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Fistix.TaskManager.Core.DomainModel.Aggregates;
using Fistix.TaskManager.Core.DomainModel.Constants;
using Fistix.TaskManager.ViewModel.Dtos;

namespace Fistix.TaskManager.ServiceLayer.Todos;

public static class AiBatchJobMapper
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static AiBatchJobDto ToDto(AiBatchJob job)
    {
        var ids = DeserializeIds(job.TodoExternalIdsJson);
        var steps = ParseSteps(job.StepsCsv);
        var percent = job.Total <= 0
            ? 0
            : Math.Round(100.0 * (job.Completed + job.Failed + job.Skipped) / (job.Total * Math.Max(1, steps.Count)), 1);

        // Better percent: progress across all step×item units
        var totalUnits = job.Total * Math.Max(1, steps.Count);
        var stepIndex = Math.Max(0, steps.FindIndex(s =>
            string.Equals(s, job.CurrentStep, StringComparison.OrdinalIgnoreCase)));
        if (stepIndex < 0)
        {
            stepIndex = 0;
        }

        var unitsDone = (stepIndex * job.Total) + job.Cursor;
        percent = totalUnits <= 0 ? 0 : Math.Round(100.0 * Math.Min(unitsDone, totalUnits) / totalUnits, 1);

        return new AiBatchJobDto
        {
            ExternalId = job.ExternalId,
            Status = job.Status,
            CurrentStep = job.CurrentStep,
            Steps = steps,
            Cursor = job.Cursor,
            Total = job.Total,
            Completed = job.Completed,
            Failed = job.Failed,
            Skipped = job.Skipped,
            BatchSize = job.BatchSize,
            DelayMsBetweenItems = job.DelayMsBetweenItems,
            OnlyMissing = job.OnlyMissing,
            ImportTag = job.ImportTag,
            LastError = job.LastError,
            LastTodoExternalId = job.LastTodoExternalId,
            PercentComplete = percent,
            CreatedAt = job.CreatedAt,
            UpdatedAt = job.UpdatedAt,
            HeartbeatAt = job.HeartbeatAt,
            PausedAt = job.PausedAt,
            CompletedAt = job.CompletedAt
        };
    }

    public static List<Guid> DeserializeIds(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        return JsonSerializer.Deserialize<List<Guid>>(json, JsonOptions) ?? [];
    }

    public static string SerializeIds(IEnumerable<Guid> ids) =>
        JsonSerializer.Serialize(ids.ToList(), JsonOptions);

    public static List<string> ParseSteps(string stepsCsv)
    {
        if (string.IsNullOrWhiteSpace(stepsCsv))
        {
            return AiBatchStepNames.DefaultSteps.ToList();
        }

        return stepsCsv
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(AiBatchStepNames.Normalize)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string StepsToCsv(IEnumerable<string> steps) =>
        string.Join(',', steps.Select(AiBatchStepNames.Normalize));
}
