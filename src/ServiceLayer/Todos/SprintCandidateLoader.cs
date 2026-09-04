#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fistix.TaskManager.Core.Abstractions.Repositories;
using Fistix.TaskManager.Core.DomainModel.Aggregates;

namespace Fistix.TaskManager.ServiceLayer.Todos;

/// <summary>Deterministic candidate loading for sprint planning (excludes active-sprint todos).</summary>
public sealed class SprintCandidateLoader
{
    private readonly ITodoTaskRepository _todoTaskRepository;
    private readonly ISprintRepository _sprintRepository;

    public SprintCandidateLoader(
        ITodoTaskRepository todoTaskRepository,
        ISprintRepository sprintRepository)
    {
        _todoTaskRepository = todoTaskRepository;
        _sprintRepository = sprintRepository;
    }

    public async Task<SprintWorkflowRequest> LoadAsync(
        Guid ownerId,
        int maxTasks,
        int durationDays,
        string? name,
        CancellationToken cancellationToken)
    {
        var todos = await _todoTaskRepository.GetByOwner(ownerId, cancellationToken);
        var inActiveSprint = await GetTodoIdsInActiveSprintsAsync(ownerId, cancellationToken);

        var candidates = todos
            .Where(SprintPlanningTools.IsCandidate)
            .Where(t => !inActiveSprint.Contains(t.Id))
            .OrderBy(t => string.Equals(t.Priority, "High", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(t => t.DueDate)
            .ToList();

        var today = DateTime.UtcNow.Date;
        var windowEnd = today.AddDays(durationDays);
        var stats = new SprintWorkloadStats
        {
            TotalCandidates = candidates.Count,
            Overdue = candidates.Count(t => t.DueDate.Date < today),
            DueInSprintWindow = candidates.Count(t =>
                t.DueDate.Date >= today && t.DueDate.Date < windowEnd),
            ExcludedInActiveSprint = inActiveSprint.Count
        };

        return new SprintWorkflowRequest
        {
            OwnerId = ownerId,
            MaxTasks = maxTasks,
            DurationDays = durationDays,
            Name = name,
            Candidates = candidates,
            Stats = stats
        };
    }

    private async Task<HashSet<int>> GetTodoIdsInActiveSprintsAsync(Guid ownerId, CancellationToken cancellationToken)
    {
        var sprints = await _sprintRepository.GetByOwner(ownerId, cancellationToken);
        var today = DateTime.UtcNow.Date;
        return sprints
            .Where(s => s.EndDate.Date >= today)
            .SelectMany(s => s.SprintTodos)
            .Select(st => st.TodoId)
            .ToHashSet();
    }
}
