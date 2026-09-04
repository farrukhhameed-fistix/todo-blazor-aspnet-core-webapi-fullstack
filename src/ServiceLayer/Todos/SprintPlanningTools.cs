#nullable enable

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Fistix.TaskManager.AiLayer.Observability;
using Fistix.TaskManager.AiLayer.Shared;
using Fistix.TaskManager.Core.Abstractions.Repositories;
using Fistix.TaskManager.Core.DomainModel.Aggregates;
using Fistix.TaskManager.ViewModel.Commands.Todos;
using Fistix.TaskManager.ViewModel.Dtos;

namespace Fistix.TaskManager.ServiceLayer.Todos;

/// <summary>
/// Tools for the MAF sprint planning agent. Scoped per optimize request via <see cref="ConfigureAsync"/>.
/// </summary>
public sealed class SprintPlanningTools
{
    private readonly ITodoTaskRepository _todoTaskRepository;
    private readonly ISprintRepository _sprintRepository;
    private readonly IAiTelemetry _telemetry;

    private Guid _ownerId;
    private int _maxTasks = 12;
    private int _durationDays = 14;
    private string _sprintName = string.Empty;
    private IReadOnlyList<TodoTask> _candidates = [];
    private int _maxToolInvocations = 12;
    private int _toolInvocationCount;

    public SprintPlanningTools(
        ITodoTaskRepository todoTaskRepository,
        ISprintRepository sprintRepository,
        IAiTelemetry? telemetry = null)
    {
        _todoTaskRepository = todoTaskRepository;
        _sprintRepository = sprintRepository;
        _telemetry = telemetry ?? NullAiTelemetry.Instance;
    }

    public Guid? CreatedSprintId { get; private set; }
    public string? CreatedSprintName { get; private set; }
    public DateTime? CreatedStartDate { get; private set; }
    public DateTime? CreatedEndDate { get; private set; }
    public List<TodoTask> SelectedTodos { get; } = [];
    public string LastProposeReasoning { get; private set; } = string.Empty;
    public List<AgentStepDto> Steps { get; } = [];
    public bool BudgetExceeded { get; private set; }
    public int ToolInvocationCount => _toolInvocationCount;

    /// <summary>When true, tool steps are labeled Analyst vs Planner; otherwise SprintAgent.</summary>
    public bool UseMultiAgentLabels { get; private set; }

    private string _activeAgentRole = string.Empty;

    /// <summary>Labels subsequent tool steps (Analyst or Planner) in multi-agent mode.</summary>
    public void SetActiveAgentRole(string role) => _activeAgentRole = role;

    /// <summary>Top candidate external ids (for planner prompt grounding).</summary>
    public IReadOnlyList<Guid> CandidateExternalIds =>
        _candidates.Select(t => t.ExternalId).ToList();

    public async Task ConfigureAsync(
        Guid ownerId,
        int maxTasks,
        int durationDays,
        string? name,
        bool multiAgent,
        CancellationToken cancellationToken,
        int maxToolInvocations = 12)
    {
        _ownerId = ownerId;
        _maxTasks = Math.Clamp(maxTasks, 1, 50);
        _durationDays = Math.Clamp(durationDays, 1, 90);
        _maxToolInvocations = Math.Max(1, maxToolInvocations);
        UseMultiAgentLabels = multiAgent;
        var start = DateTime.UtcNow.Date;
        _sprintName = string.IsNullOrWhiteSpace(name)
            ? $"Optimized Sprint {start:yyyy-MM-dd}"
            : name.Trim();

        CreatedSprintId = null;
        CreatedSprintName = null;
        CreatedStartDate = null;
        CreatedEndDate = null;
        SelectedTodos.Clear();
        LastProposeReasoning = string.Empty;
        Steps.Clear();
        BudgetExceeded = false;
        _toolInvocationCount = 0;

        var todos = await _todoTaskRepository.GetByOwner(ownerId, cancellationToken);
        ApplyCandidates(todos, ownerId, maxTasks, durationDays, name, multiAgent, maxToolInvocations);
    }

    public void ConfigureFromWorkflow(
        SprintWorkflowRequest request,
        bool multiAgent,
        int maxToolInvocations = 12)
    {
        ApplyCandidates(
            request.Candidates.ToList(),
            request.OwnerId,
            request.MaxTasks,
            request.DurationDays,
            request.Name,
            multiAgent,
            maxToolInvocations);
    }

    private void ApplyCandidates(
        List<TodoTask> todos,
        Guid ownerId,
        int maxTasks,
        int durationDays,
        string? name,
        bool multiAgent,
        int maxToolInvocations)
    {
        _ownerId = ownerId;
        _maxTasks = Math.Clamp(maxTasks, 1, 50);
        _durationDays = Math.Clamp(durationDays, 1, 90);
        _maxToolInvocations = Math.Max(1, maxToolInvocations);
        UseMultiAgentLabels = multiAgent;
        var start = DateTime.UtcNow.Date;
        _sprintName = string.IsNullOrWhiteSpace(name)
            ? $"Optimized Sprint {start:yyyy-MM-dd}"
            : name.Trim();

        CreatedSprintId = null;
        CreatedSprintName = null;
        CreatedStartDate = null;
        CreatedEndDate = null;
        SelectedTodos.Clear();
        LastProposeReasoning = string.Empty;
        Steps.Clear();
        BudgetExceeded = false;
        _toolInvocationCount = 0;

        _candidates = todos
            .Where(IsCandidate)
            .OrderBy(t => string.Equals(t.Priority, "High", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(t => t.DueDate)
            .ToList();
    }

    [Description("Search incomplete High/Medium priority todos available for sprint planning.")]
    public string SearchIncompleteTodos()
    {
        var payload = _candidates.Select(Summarize).ToList();
        RecordStep("Analyst", "search_incomplete_todos", $"Found {payload.Count} candidate todos.");
        return JsonSerializer.Serialize(new { count = payload.Count, todos = payload });
    }

    [Description("Get workload statistics for incomplete High/Medium todos (counts by priority and status).")]
    public string GetWorkloadStats()
    {
        var byPriority = _candidates
            .GroupBy(t => t.Priority ?? "Unknown", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        var byStatus = _candidates
            .GroupBy(t => t.Status ?? "Unknown", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        var overdue = _candidates.Count(t => t.DueDate.Date < DateTime.UtcNow.Date);
        var dueSoon = _candidates.Count(t =>
        {
            var due = t.DueDate.Date;
            var today = DateTime.UtcNow.Date;
            return due >= today && due < today.AddDays(_durationDays);
        });

        RecordStep("Analyst", "get_workload_stats", $"candidates={_candidates.Count}, overdue={overdue}, dueSoon={dueSoon}");
        return JsonSerializer.Serialize(new
        {
            totalCandidates = _candidates.Count,
            byPriority,
            byStatus,
            overdue,
            dueInSprintWindow = dueSoon,
            sprintDurationDays = _durationDays,
            maxTasks = _maxTasks
        });
    }

    [Description("Find incomplete High/Medium todos due within the sprint duration window starting today (UTC).")]
    public string FindDueSoonTodos()
    {
        var today = DateTime.UtcNow.Date;
        var end = today.AddDays(_durationDays);
        var dueSoon = _candidates
            .Where(t => t.DueDate.Date >= today && t.DueDate.Date < end)
            .Select(Summarize)
            .ToList();

        RecordStep("Analyst", "find_due_soon_todos", $"Found {dueSoon.Count} todos due in next {_durationDays} days.");
        return JsonSerializer.Serialize(new { count = dueSoon.Count, todos = dueSoon });
    }

    [Description("Validate a proposed sprint task selection. Pass comma-separated todo external ids and a brief reasoning.")]
    public string ProposeSprintPlan(
        [Description("Comma-separated todo external GUIDs selected for the sprint")] string todoExternalIds,
        [Description("Brief reasoning for the selection and grouping")] string reasoning)
    {
        var ids = ParseIds(todoExternalIds);
        var byId = _candidates.ToDictionary(t => t.ExternalId);
        var selected = new List<TodoTask>();
        var rejected = new List<string>();

        foreach (var id in ids.Distinct())
        {
            if (!byId.TryGetValue(id, out var todo))
            {
                rejected.Add(id.ToString());
                continue;
            }

            selected.Add(todo);
            if (selected.Count >= _maxTasks)
            {
                break;
            }
        }

        SelectedTodos.Clear();
        SelectedTodos.AddRange(selected);
        LastProposeReasoning = LlmOutputValidator.ValidateAgentText(
            string.IsNullOrWhiteSpace(reasoning) ? "Proposed via agent tool." : reasoning);

        RecordStep(
            "Planner",
            "propose_sprint_plan",
            $"accepted={selected.Count}, rejected={rejected.Count}, max={_maxTasks}");

        return JsonSerializer.Serialize(new
        {
            acceptedCount = selected.Count,
            rejectedUnknownIds = rejected,
            maxTasks = _maxTasks,
            reasoning = LastProposeReasoning,
            selected = selected.Select(Summarize)
        });
    }

    [Description("Create and persist the sprint using the last successfully proposed task selection.")]
    public async Task<string> CreateSprint(CancellationToken cancellationToken = default)
    {
        if (SelectedTodos.Count == 0)
        {
            RecordStep("Planner", "create_sprint", "Failed: no proposed tasks.");
            return JsonSerializer.Serialize(new { ok = false, error = "Call propose_sprint_plan with valid todo ids first." });
        }

        if (CreatedSprintId.HasValue)
        {
            RecordStep("Planner", "create_sprint", $"Already created {CreatedSprintId}");
            return JsonSerializer.Serialize(new { ok = true, sprintId = CreatedSprintId, name = CreatedSprintName });
        }

        var startDate = DateTime.UtcNow.Date;
        var endDate = startDate.AddDays(_durationDays);
        var sprint = new Sprint
        {
            Name = _sprintName,
            StartDate = startDate,
            EndDate = endDate,
            CreatedByUserId = _ownerId,
            CreatedAt = DateTime.UtcNow,
            Reasoning = LastProposeReasoning
        };
        sprint.GenerateNewExternalId();

        foreach (var todo in SelectedTodos)
        {
            sprint.SprintTodos.Add(new SprintTodo { TodoId = todo.Id });
        }

        await _sprintRepository.Create(sprint, cancellationToken);
        CreatedSprintId = sprint.ExternalId;
        CreatedSprintName = sprint.Name;
        CreatedStartDate = sprint.StartDate;
        CreatedEndDate = sprint.EndDate;

        RecordStep("Planner", "create_sprint", $"Created sprint {sprint.ExternalId} with {SelectedTodos.Count} tasks.");
        return JsonSerializer.Serialize(new
        {
            ok = true,
            sprintId = sprint.ExternalId,
            name = sprint.Name,
            startDate = sprint.StartDate,
            endDate = sprint.EndDate,
            taskCount = SelectedTodos.Count
        });
    }

    public static bool IsCandidate(TodoTask todo)
    {
        if (string.Equals(todo.Status, "Completed", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return string.Equals(todo.Priority, "High", StringComparison.OrdinalIgnoreCase)
            || string.Equals(todo.Priority, "Medium", StringComparison.OrdinalIgnoreCase);
    }

    private void RecordStep(string defaultRole, string toolName, string summary)
    {
        _toolInvocationCount++;
        if (_toolInvocationCount > _maxToolInvocations)
        {
            BudgetExceeded = true;
            _telemetry.RecordQualityEvent(
                AiTelemetryNames.Features.SprintOptimizer,
                AiTelemetryNames.QualityEvents.BudgetExceeded);
            throw new InvalidOperationException(
                $"Sprint agent tool budget exceeded ({_maxToolInvocations} invocations).");
        }

        var agentName = UseMultiAgentLabels
            ? (string.IsNullOrWhiteSpace(_activeAgentRole) ? defaultRole : _activeAgentRole)
            : "SprintAgent";

        Steps.Add(new AgentStepDto
        {
            AgentName = agentName,
            ToolName = toolName,
            Summary = summary
        });

        // Lightweight tool span for Aspire (args omitted; summary is already a safe short string).
        var activity = _telemetry.StartToolCall(toolName, summary);
        _telemetry.CompleteToolCall(activity, durationMs: 0, success: true);
    }

    private static List<Guid> ParseIds(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        return raw
            .Split([',', ' ', ';', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries)
            .Select(s => Guid.TryParse(s.Trim().Trim('"'), out var g) ? g : (Guid?)null)
            .Where(g => g.HasValue)
            .Select(g => g!.Value)
            .ToList();
    }

    private static object Summarize(TodoTask t) => new
    {
        id = t.ExternalId,
        title = t.Title,
        priority = t.Priority,
        status = t.Status,
        due = t.DueDate.ToString("yyyy-MM-dd"),
        category = t.Category
    };
}
