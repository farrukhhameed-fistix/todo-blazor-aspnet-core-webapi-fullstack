#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Fistix.TaskManager.Core.DomainModel.Aggregates;

namespace Fistix.TaskManager.ServiceLayer.Todos;

/// <summary>Non-LLM critic: trims analyst recommendations and adds rule-based risks.</summary>
public static class SprintCapacityCritic
{
    public static AnalystOutput Apply(
        AnalystOutput analyst,
        IReadOnlyList<TodoTask> candidates,
        int maxTasks,
        int durationDays)
    {
        var byId = candidates.ToDictionary(t => t.ExternalId);
        var ordered = analyst.RecommendedIds
            .Where(byId.ContainsKey)
            .Select(id => byId[id])
            .Concat(candidates.Where(c => !analyst.RecommendedIds.Contains(c.ExternalId)))
            .DistinctBy(t => t.ExternalId)
            .Take(Math.Clamp(maxTasks, 1, 50))
            .ToList();

        var risks = analyst.Risks.ToList();
        var today = DateTime.UtcNow.Date;
        var windowEnd = today.AddDays(durationDays);
        var dueSoon = ordered.Count(t => t.DueDate.Date >= today && t.DueDate.Date < windowEnd);
        if (dueSoon == 0 && ordered.Count > 0)
        {
            risks.Add("Selected set has no tasks due within the sprint window.");
        }

        if (ordered.Count < Math.Min(maxTasks, candidates.Count))
        {
            risks.Add($"Capped selection to {ordered.Count} tasks (max {maxTasks}).");
        }

        return new AnalystOutput
        {
            RecommendedIds = ordered.Select(t => t.ExternalId).ToList(),
            Risks = risks.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Theme = analyst.Theme,
            Summary = analyst.Summary,
            Stats = analyst.Stats
        };
    }
}
