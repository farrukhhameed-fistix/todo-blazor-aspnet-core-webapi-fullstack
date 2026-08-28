#nullable enable

using Fistix.TaskManager.AiLayer.Agents;
using Fistix.TaskManager.AiLayer.Observability;
using Fistix.TaskManager.AiLayer.Shared;
using Fistix.TaskManager.Core.Abstractions.Repositories;
using Fistix.TaskManager.Core.DomainModel.Aggregates;
using Fistix.TaskManager.Core.DomainModel.Constants;
using Fistix.TaskManager.ViewModel.Commands.Todos;
using Fistix.TaskManager.ViewModel.Dtos;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Fistix.TaskManager.ServiceLayer.Todos;

/// <summary>
/// Microsoft Agent Framework sprint planner.
/// Default: Analyst → Planner sequential workflow. Optional single-agent mode via Ai:Agents:WorkflowMode.
/// Falls back to heuristic selection when the agent run fails.
/// </summary>
public class SprintOptimizerAgent
{
    private const string AnalystInstructions = """
        You are the sprint workload Analyst for a task manager.
        You MUST use tools — do not invent todo GUIDs.
        Call search_incomplete_todos, get_workload_stats, and find_due_soon_todos.
        Then write a concise analysis for the Planner that includes:
        - Recommended todo external ids (comma-separated, real ids only)
        - Risks (overdue, overload, missing due-soon coverage)
        - Suggested sprint theme / grouping
        Do NOT call propose_sprint_plan or create_sprint — that is the Planner's job.
        """;

    private const string PlannerInstructions = """
        You are the sprint Planner for a task manager.
        You receive the Analyst's report and a list of valid todo external GUIDs.
        You MUST call tools — do not invent todo GUIDs and do not finish with text only.
        REQUIRED step:
        - propose_sprint_plan — comma-separated real todo external GUIDs from the Analyst report and a brief reasoning
        Do NOT call create_sprint — the user approves proposals before anything is persisted.
        Use only ids that appear in the Analyst report or the valid-id list provided in the user message.
        Respect max task and duration constraints.
        After propose_sprint_plan succeeds, give a one-sentence summary.
        """;

    private const string SingleAgentInstructions = """
        You are a sprint planning agent for a task manager.
        You MUST use tools — do not invent todo GUIDs.
        Suggested flow:
        1) search_incomplete_todos
        2) get_workload_stats and/or find_due_soon_todos
        3) propose_sprint_plan with a comma-separated list of real todo ids and brief reasoning
        Do NOT call create_sprint — the user approves proposals before anything is persisted.
        Prefer High priority, earlier due dates, and thematic grouping.
        Respect max task and duration constraints exposed by the tools.
        After propose_sprint_plan succeeds, give a short final summary for the user.
        """;

    private readonly AiChatClientFactory _chatClientFactory;
    private readonly SprintPlanningTools _tools;
    private readonly ITodoTaskRepository _todoTaskRepository;
    private readonly AiConfiguration _aiConfig;
    private readonly IAiTelemetry _telemetry;
    private readonly ILogger<SprintOptimizerAgent> _logger;

    public SprintOptimizerAgent(
        AiChatClientFactory chatClientFactory,
        SprintPlanningTools tools,
        ITodoTaskRepository todoTaskRepository,
        AiConfiguration aiConfig,
        ILogger<SprintOptimizerAgent> logger,
        IAiTelemetry? telemetry = null)
    {
        _chatClientFactory = chatClientFactory;
        _tools = tools;
        _todoTaskRepository = todoTaskRepository;
        _aiConfig = aiConfig;
        _logger = logger;
        _telemetry = telemetry ?? NullAiTelemetry.Instance;
    }

    public async Task<SprintOptimizationPlan> PlanAsync(
        Guid ownerId,
        int maxTasks,
        int durationDays,
        string? name,
        CancellationToken cancellationToken,
        Func<string, string?, CancellationToken, Task>? onPhaseChanged = null)
    {
        var multi = IsMultiAgentMode();
        var maxToolInvocations = Math.Max(1, _aiConfig.Agents?.MaxToolInvocationsPerJob ?? 12);
        await _tools.ConfigureAsync(
            ownerId,
            maxTasks,
            durationDays,
            name,
            multi,
            cancellationToken,
            maxToolInvocations);

        try
        {
            var chatClient = _chatClientFactory.CreateChatClient();
            var goal = BuildGoal(maxTasks, durationDays, name);

            AgentResponse response = multi
                ? await RunMultiAgentWorkflowAsync(chatClient, goal, maxTasks, durationDays, name, cancellationToken, onPhaseChanged)
                : await RunSingleAgentAsync(chatClient, goal, cancellationToken, onPhaseChanged);

            EnsureToolStepsPresent(response);

            var recoveryPasses = Math.Max(0, _aiConfig.Agents?.MaxPlannerRecoveryPasses ?? 1);
            if (_tools.SelectedTodos.Count == 0 && multi && recoveryPasses > 0 && !_tools.BudgetExceeded)
            {
                for (var pass = 0; pass < recoveryPasses && _tools.SelectedTodos.Count == 0; pass++)
                {
                    _logger.LogWarning(
                        "Planner did not select tasks after workflow; recovery pass {Pass}/{Max}. Steps: {Steps}",
                        pass + 1,
                        recoveryPasses,
                        string.Join(" → ", _tools.Steps.Select(s => $"{s.AgentName}/{s.ToolName}")));

                    await TryRecoverPlannerSelectionAsync(chatClient, goal, maxTasks, durationDays, name, cancellationToken);
                    if (_tools.BudgetExceeded)
                    {
                        break;
                    }
                }
            }

            if (_tools.SelectedTodos.Count > 0)
            {
                return BuildPlanFromTools(response.Text);
            }

            _logger.LogWarning("MAF sprint agent did not select tasks; falling back to heuristic.");
        }
        catch (System.ClientModel.ClientResultException ex)
        {
            var body = TryReadErrorBody(ex);
            _logger.LogWarning(
                ex,
                "MAF sprint agent failed with HTTP {Status}; falling back to heuristic. Body: {Body}",
                ex.Status,
                body);
        }
        catch (InvalidOperationException ex) when (_tools.BudgetExceeded ||
            ex.Message.Contains("tool budget exceeded", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(ex, "Sprint agent over budget; falling back to heuristic");
            _telemetry.RecordQualityEvent(
                AiTelemetryNames.Features.SprintOptimizer,
                AiTelemetryNames.QualityEvents.BudgetExceeded);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MAF sprint agent failed; falling back to heuristic selection");
        }

        if (onPhaseChanged is not null)
        {
            await onPhaseChanged(
                SprintOptimizerPhase.Planner,
                _tools.BudgetExceeded
                    ? "Tool budget exceeded; using heuristic fallback selection."
                    : "Using heuristic fallback selection.",
                cancellationToken);
        }

        return await HeuristicFallbackAsync(ownerId, maxTasks, durationDays, cancellationToken);
    }

    private async Task<AgentResponse> RunMultiAgentWorkflowAsync(
        IChatClient chatClient,
        string goal,
        int maxTasks,
        int durationDays,
        string? name,
        CancellationToken cancellationToken,
        Func<string, string?, CancellationToken, Task>? onPhaseChanged)
    {
        _logger.LogInformation("Running MAF Analyst → Planner sequential workflow");

        AIAgent analyst = chatClient.AsAIAgent(
            instructions: AnalystInstructions,
            name: "SprintAnalyst",
            description: "Analyzes incomplete todos and workload for sprint planning.",
            tools:
            [
                AIFunctionFactory.Create(_tools.SearchIncompleteTodos),
                AIFunctionFactory.Create(_tools.GetWorkloadStats),
                AIFunctionFactory.Create(_tools.FindDueSoonTodos)
            ]);

        _tools.Steps.Add(new AgentStepDto
        {
            AgentName = "Workflow",
            ToolName = "sequential_start",
            Summary = "Analyst → Planner"
        });

        if (onPhaseChanged is not null)
        {
            await onPhaseChanged(SprintOptimizerPhase.Analyst, "Analyst is reviewing workload…", cancellationToken);
        }

        _tools.SetActiveAgentRole("Analyst");
        var analystResponse = await analyst.RunAsync(goal, cancellationToken: cancellationToken);
        EnsureToolStepsPresent(analystResponse);

        var analystBrief = LlmOutputValidator.ValidateAgentText(
            string.IsNullOrWhiteSpace(analystResponse.Text)
                ? "Analyst did not return a summary. Prioritize High priority and earliest due dates."
                : analystResponse.Text);

        _logger.LogInformation(
            "Analyst phase complete. Tool steps={StepCount}, brief length={BriefLength}",
            _tools.Steps.Count,
            analystBrief.Length);

        cancellationToken.ThrowIfCancellationRequested();

        var plannerGoal = BuildPlannerGoal(goal, analystBrief, maxTasks);

        if (onPhaseChanged is not null)
        {
            await onPhaseChanged(SprintOptimizerPhase.Planner, "Planner is selecting tasks for proposal…", cancellationToken);
        }

        _tools.SetActiveAgentRole("Planner");
        AIAgent planner = chatClient.AsAIAgent(
            instructions: PlannerInstructions,
            name: "SprintPlanner",
            description: "Proposes and creates a sprint from the Analyst report.",
            tools:
            [
                AIFunctionFactory.Create(_tools.SearchIncompleteTodos),
                AIFunctionFactory.Create(_tools.ProposeSprintPlan)
            ]);

        var plannerResponse = await planner.RunAsync(plannerGoal, cancellationToken: cancellationToken);
        EnsureToolStepsPresent(plannerResponse);

        _logger.LogInformation(
            "Planner phase complete. Selected={Selected}",
            _tools.SelectedTodos.Count);

        return plannerResponse;
    }

    private async Task TryRecoverPlannerSelectionAsync(
        IChatClient chatClient,
        string goal,
        int maxTasks,
        int durationDays,
        string? name,
        CancellationToken cancellationToken)
    {
        var candidateIds = _tools.CandidateExternalIds;
        if (candidateIds.Count == 0)
        {
            return;
        }

        var topIds = candidateIds.Take(Math.Clamp(maxTasks, 1, 50));
        var idLine = string.Join(", ", topIds);

        var recoveryGoal = $"""
            {goal}

            The Planner must call tools now. Valid todo external GUIDs (pick up to {maxTasks}):
            {idLine}

            Call propose_sprint_plan with a comma-separated subset of these ids.
            Do not respond without calling propose_sprint_plan.
            """;

        AIAgent planner = chatClient.AsAIAgent(
            instructions: PlannerInstructions,
            name: "SprintPlannerRecovery",
            description: "Recovery planner pass with explicit candidate ids.",
            tools:
            [
                AIFunctionFactory.Create(_tools.ProposeSprintPlan)
            ]);

        _tools.Steps.Add(new AgentStepDto
        {
            AgentName = "Planner",
            ToolName = "recovery_pass",
            Summary = $"Retry with {topIds.Count()} explicit candidate ids."
        });

        _tools.SetActiveAgentRole("Planner");
        var recoveryResponse = await planner.RunAsync(recoveryGoal, cancellationToken: cancellationToken);
        EnsureToolStepsPresent(recoveryResponse);
    }

    private string BuildPlannerGoal(string goal, string analystBrief, int maxTasks)
    {
        var idHint = _tools.CandidateExternalIds.Count == 0
            ? "No candidates loaded."
            : string.Join(", ", _tools.CandidateExternalIds.Take(Math.Min(_tools.CandidateExternalIds.Count, maxTasks * 2)));

        return $"""
            {goal}

            --- Analyst report ---
            {analystBrief}

            --- Valid todo external GUIDs (use only these in propose_sprint_plan, max {maxTasks}) ---
            {idHint}

            Call propose_sprint_plan before finishing.
            """;
    }

    private async Task<AgentResponse> RunSingleAgentAsync(
        IChatClient chatClient,
        string goal,
        CancellationToken cancellationToken,
        Func<string, string?, CancellationToken, Task>? onPhaseChanged)
    {
        _logger.LogInformation("Running MAF single sprint planning agent");

        if (onPhaseChanged is not null)
        {
            await onPhaseChanged(SprintOptimizerPhase.Planner, "Sprint agent is planning…", cancellationToken);
        }

        AIAgent agent = chatClient.AsAIAgent(
            instructions: SingleAgentInstructions,
            name: "SprintPlanningAgent",
            description: "Plans and creates an optimized sprint using todo tools.",
            tools:
            [
                AIFunctionFactory.Create(_tools.SearchIncompleteTodos),
                AIFunctionFactory.Create(_tools.GetWorkloadStats),
                AIFunctionFactory.Create(_tools.FindDueSoonTodos),
                AIFunctionFactory.Create(_tools.ProposeSprintPlan)
            ]);

        return await agent.RunAsync(goal, cancellationToken: cancellationToken);
    }

    private bool IsMultiAgentMode()
    {
        var mode = _aiConfig.Agents?.WorkflowMode?.Trim() ?? "Multi";
        return !string.Equals(mode, "Single", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildGoal(int maxTasks, int durationDays, string? name) =>
        $"Plan and create a sprint lasting {durationDays} days with at most {maxTasks} tasks. " +
        (string.IsNullOrWhiteSpace(name) ? "" : $"Use sprint name '{name.Trim()}'. ") +
        "Analyst: inspect workload and recommend real todo ids. Planner: propose_sprint_plan only (user approves before persist).";

    private SprintOptimizationPlan BuildPlanFromTools(string? agentText) =>
        new()
        {
            SelectedTodos = _tools.SelectedTodos.ToList(),
            Reasoning = BuildReasoning(agentText, _tools.LastProposeReasoning),
            Steps = _tools.Steps.ToList()
        };

    private async Task<SprintOptimizationPlan> HeuristicFallbackAsync(
        Guid ownerId,
        int maxTasks,
        int durationDays,
        CancellationToken cancellationToken)
    {
        var todos = await _todoTaskRepository.GetByOwner(ownerId, cancellationToken);
        var selected = todos
            .Where(SprintPlanningTools.IsCandidate)
            .OrderBy(t => string.Equals(t.Priority, "High", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(t => t.DueDate)
            .Take(Math.Clamp(maxTasks, 1, 50))
            .ToList();

        var reasoning =
            $"Selected {selected.Count} high/medium tasks for a {durationDays}-day sprint " +
            "using priority and due-date ordering (agent unavailable or invalid tool outcome).";

        var steps = _tools.Steps.ToList();
        steps.Add(new AgentStepDto
        {
            AgentName = "Heuristic",
            ToolName = "heuristic_fallback",
            Summary = reasoning
        });

        return new SprintOptimizationPlan
        {
            SelectedTodos = selected,
            Reasoning = reasoning,
            Steps = steps
        };
    }

    private void EnsureToolStepsPresent(AgentResponse response)
    {
        if (response.Messages is null)
        {
            return;
        }

        foreach (var message in response.Messages)
        {
            foreach (var content in message.Contents)
            {
                if (content is not FunctionCallContent call || string.IsNullOrWhiteSpace(call.Name))
                {
                    continue;
                }

                if (!_tools.Steps.Any(s => string.Equals(s.ToolName, call.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    _tools.Steps.Add(new AgentStepDto
                    {
                        AgentName = _tools.UseMultiAgentLabels ? "Workflow" : "SprintAgent",
                        ToolName = call.Name,
                        Summary = "Invoked by agent."
                    });
                }
            }
        }
    }

    private static string BuildReasoning(string? agentText, string proposeReasoning)
    {
        if (!string.IsNullOrWhiteSpace(agentText))
        {
            return agentText.Trim();
        }

        return string.IsNullOrWhiteSpace(proposeReasoning)
            ? "Sprint planned by agent."
            : proposeReasoning;
    }

    private static string TryReadErrorBody(System.ClientModel.ClientResultException ex)
    {
        try
        {
            var contentProp = ex.GetType().GetProperty("Content");
            if (contentProp?.GetValue(ex) is BinaryData data)
            {
                var text = data.ToString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text.Length > 2000 ? text[..2000] + "…" : text;
                }
            }
        }
        catch
        {
            // ignore reflection failures
        }

        return ex.Message;
    }
}

public class SprintOptimizationPlan
{
    public List<TodoTask> SelectedTodos { get; set; } = [];
    public string Reasoning { get; set; } = string.Empty;
    public List<AgentStepDto> Steps { get; set; } = [];
}
