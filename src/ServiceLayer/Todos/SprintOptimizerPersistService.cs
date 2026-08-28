#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fistix.TaskManager.Core.Abstractions.Repositories;
using Fistix.TaskManager.Core.DomainModel.Aggregates;
using Fistix.TaskManager.ViewModel.Commands.Todos;
using Fistix.TaskManager.ViewModel.Dtos;

namespace Fistix.TaskManager.ServiceLayer.Todos;

/// <summary>
/// Builds sprint proposals from agent output and persists approved sprints (idempotent per job).
/// </summary>
public sealed class SprintOptimizerPersistService
{
    private readonly ITodoTaskRepository _todoTaskRepository;
    private readonly ISprintRepository _sprintRepository;

    public SprintOptimizerPersistService(
        ITodoTaskRepository todoTaskRepository,
        ISprintRepository sprintRepository)
    {
        _todoTaskRepository = todoTaskRepository;
        _sprintRepository = sprintRepository;
    }

    public SprintOptimizerProposalDto BuildProposal(SprintOptimizationPlan plan, bool usedHeuristicFallback)
    {
        return new SprintOptimizerProposalDto
        {
            SelectedTasks = plan.SelectedTodos.Select(ToSummary).ToList(),
            Reasoning = plan.Reasoning,
            Steps = plan.Steps.ToList(),
            UsedHeuristicFallback = usedHeuristicFallback,
            ProposedAt = DateTime.UtcNow
        };
    }

    public async Task<IReadOnlyList<TodoTask>> ResolveSelectedTodosAsync(
        Guid ownerId,
        IReadOnlyList<Guid> todoExternalIds,
        int maxTasks,
        CancellationToken cancellationToken)
    {
        if (todoExternalIds.Count == 0)
        {
            return [];
        }

        var todos = await _todoTaskRepository.GetByOwner(ownerId, cancellationToken);
        var candidates = todos
            .Where(SprintPlanningTools.IsCandidate)
            .ToDictionary(t => t.ExternalId);

        var selected = new List<TodoTask>();
        foreach (var id in todoExternalIds.Distinct())
        {
            if (!candidates.TryGetValue(id, out var todo))
            {
                continue;
            }

            selected.Add(todo);
            if (selected.Count >= Math.Clamp(maxTasks, 1, 50))
            {
                break;
            }
        }

        return selected;
    }

    public async Task<OptimizeSprintResponseDto> PersistApprovedAsync(
        SprintOptimizerJob job,
        IReadOnlyList<TodoTask> selectedTodos,
        string reasoning,
        IReadOnlyList<AgentStepDto> steps,
        CancellationToken cancellationToken)
    {
        if (selectedTodos.Count == 0)
        {
            throw new InvalidOperationException("Cannot create a sprint with no selected tasks.");
        }

        if (job.CreatedSprintId.HasValue)
        {
            return BuildResponseFromExisting(job, selectedTodos, reasoning, steps);
        }

        var startDate = DateTime.UtcNow.Date;
        var endDate = startDate.AddDays(job.DurationDays);
        var sprintName = string.IsNullOrWhiteSpace(job.Name)
            ? $"Optimized Sprint {startDate:yyyy-MM-dd}"
            : job.Name.Trim();

        var sprint = new Sprint
        {
            Name = sprintName,
            StartDate = startDate,
            EndDate = endDate,
            CreatedByUserId = job.CreatedByUserId,
            CreatedAt = DateTime.UtcNow,
            Reasoning = reasoning
        };
        sprint.GenerateNewExternalId();

        foreach (var todo in selectedTodos)
        {
            sprint.SprintTodos.Add(new SprintTodo { TodoId = todo.Id });
        }

        await _sprintRepository.Create(sprint, cancellationToken);
        job.CreatedSprintId = sprint.ExternalId;

        return new OptimizeSprintResponseDto
        {
            SprintId = sprint.ExternalId,
            Name = sprint.Name,
            StartDate = sprint.StartDate,
            EndDate = sprint.EndDate,
            Reasoning = reasoning,
            Steps = steps.ToList(),
            SelectedTasks = selectedTodos.Select(ToSummary).ToList()
        };
    }

    private static OptimizeSprintResponseDto BuildResponseFromExisting(
        SprintOptimizerJob job,
        IReadOnlyList<TodoTask> selectedTodos,
        string reasoning,
        IReadOnlyList<AgentStepDto> steps)
    {
        var startDate = DateTime.UtcNow.Date;
        var endDate = startDate.AddDays(job.DurationDays);
        var sprintName = string.IsNullOrWhiteSpace(job.Name)
            ? $"Optimized Sprint {startDate:yyyy-MM-dd}"
            : job.Name.Trim();

        return new OptimizeSprintResponseDto
        {
            SprintId = job.CreatedSprintId!.Value,
            Name = sprintName,
            StartDate = startDate,
            EndDate = endDate,
            Reasoning = reasoning,
            Steps = steps.ToList(),
            SelectedTasks = selectedTodos.Select(ToSummary).ToList()
        };
    }

    private static SprintTaskSummaryDto ToSummary(TodoTask t) => new()
    {
        ExternalId = t.ExternalId,
        Title = t.Title,
        Priority = t.Priority,
        Status = t.Status,
        DueDate = t.DueDate,
        Category = t.Category
    };
}
