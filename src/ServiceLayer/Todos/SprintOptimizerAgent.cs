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
/// Default: Analyst → Planner via MAF WorkflowBuilder. Optional single-agent mode via Ai:Agents:WorkflowMode.
/// Falls back to heuristic selection when the agent run fails.
/// </summary>
public class SprintOptimizerAgent
{
    private const string AnalystInstructions = """
        You are the sprint workload Analyst for a task manager.
        You MUST use tools — do not invent todo GUIDs.
        Call search_incomplete_todos, get_workload_stats, and find_due_soon_todos.
        Then write a concise analysis for the Planner.
        Respond with JSON only in this shape:
        {
          "recommendedIds": ["<todo-external-guid>", ...],
          "risks": ["..."],
          "theme": "short sprint theme",
          "summary": "one paragraph for the planner"
        }
        Use only real todo external GUIDs from tool results.
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
    private readonly SprintCandidateLoader _candidateLoader;
    private readonly SprintOptimizerWorkflowHost _workflowHost;
    private readonly ITodoTaskRepository _todoTaskRepository;
    private readonly AiConfiguration _aiConfig;
    private readonly IAiTelemetry _telemetry;
    private readonly ILogger<SprintOptimizerAgent> _logger;

    public SprintOptimizerAgent(
        AiChatClientFactory chatClientFactory,
        SprintPlanningTools tools,
        SprintCandidateLoader candidateLoader,
        SprintOptimizerWorkflowHost workflowHost,
        ITodoTaskRepository todoTaskRepository,
        AiConfiguration aiConfig,
        ILogger<SprintOptimizerAgent> logger,
        IAiTelemetry? telemetry = null)
    {
        _chatClientFactory = chatClientFactory;
        _tools = tools;
        _candidateLoader = candidateLoader;
        _workflowHost = workflowHost;
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
        Func<string, string?, AnalystOutput?, CancellationToken, Task>? onPhaseChanged = null,
        SprintOptimizerCheckpointDto? resumeFrom = null)
    {
        var multi = IsMultiAgentMode();
        var maxToolInvocations = Math.Max(1, _aiConfig.Agents?.MaxToolInvocationsPerJob ?? 12);
        var workflowRequest = await _candidateLoader.LoadAsync(
            ownerId, maxTasks, durationDays, name, cancellationToken);

        _tools.ConfigureFromWorkflow(workflowRequest, multi, maxToolInvocations);

        if (workflowRequest.Candidates.Count == 0)
        {
            _logger.LogInformation("No sprint candidates after load; skipping LLM.");
            return BuildEmptyPlan(workflowRequest.Stats);
        }

        AnalystOutput? analystOutput = null;
        try
        {
            var chatClient = _chatClientFactory.CreateChatClient();
            var goal = BuildGoal(maxTasks, durationDays, name);

            AgentResponse response;
            if (multi)
            {
                (response, analystOutput) = await RunMultiAgentWorkflowAsync(
                    chatClient, goal, maxTasks, workflowRequest, cancellationToken, onPhaseChanged, resumeFrom);
            }
            else
            {
                response = await RunSingleAgentAsync(chatClient, goal, cancellationToken, onPhaseChanged);
            }

            EnsureToolStepsPresent(response);

            var recoveryPasses = Math.Max(0, _aiConfig.Agents?.MaxPlannerRecoveryPasses ?? 1);
            if (_tools.SelectedTodos.Count == 0 && multi && recoveryPasses > 0 && !_tools.BudgetExceeded)
            {
                var recoveryIds = analystOutput?.RecommendedIds ?? _tools.CandidateExternalIds;
                for (var pass = 0; pass < recoveryPasses && _tools.SelectedTodos.Count == 0; pass++)
                {
                    _logger.LogWarning(
                        "Planner did not select tasks after workflow; recovery pass {Pass}/{Max}. Steps: {Steps}",
                        pass + 1,
                        recoveryPasses,
                        string.Join(" → ", _tools.Steps.Select(s => $"{s.AgentName}/{s.ToolName}")));

                    await TryRecoverPlannerSelectionAsync(
                        chatClient, goal, maxTasks, recoveryIds, cancellationToken);
                    if (_tools.BudgetExceeded)
                    {
                        break;
                    }
                }
            }

            if (_tools.SelectedTodos.Count > 0)
            {
                var plan = BuildPlanFromTools(response.Text);
                plan.AnalystOutput = analystOutput;
                plan.Stats = workflowRequest.Stats;
                return plan;
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
                null,
                cancellationToken);
        }

        return await HeuristicFallbackAsync(workflowRequest, cancellationToken);
    }

    private static SprintOptimizationPlan BuildEmptyPlan(SprintWorkloadStats stats)
    {
        var summary = stats.ExcludedInActiveSprint > 0
            ? $"No eligible candidates ({stats.ExcludedInActiveSprint} already in active sprints)."
            : "No eligible High/Medium incomplete todos for sprint planning.";

        return new SprintOptimizationPlan
        {
            Reasoning = summary,
            Steps =
            [
                new AgentStepDto
                {
                    AgentName = "LoadCandidates",
                    ToolName = "empty_inbox",
                    Summary = summary
                }
            ]
        };
    }

    private async Task<(AgentResponse Response, AnalystOutput AnalystOutput)> RunMultiAgentWorkflowAsync(
        IChatClient chatClient,
        string goal,
        int maxTasks,
        SprintWorkflowRequest workflowRequest,
        CancellationToken cancellationToken,
        Func<string, string?, AnalystOutput?, CancellationToken, Task>? onPhaseChanged,
        SprintOptimizerCheckpointDto? resumeFrom = null)
    {
        var (response, analystOutput) = await _workflowHost.RunSequentialAsync(
            chatClient,
            goal,
            maxTasks,
            workflowRequest,
            AnalystInstructions,
            PlannerInstructions,
            cancellationToken,
            onPhaseChanged,
            resumeFrom);

        EnsureToolStepsPresent(response);

        _logger.LogInformation(
            "MAF Analyst → Planner workflow complete. Selected={Selected}",
            _tools.SelectedTodos.Count);

        return (response, analystOutput);
    }

    private async Task TryRecoverPlannerSelectionAsync(
        IChatClient chatClient,
        string goal,
        int maxTasks,
        IReadOnlyList<Guid> candidateIds,
        CancellationToken cancellationToken)
    {
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

    private async Task<AgentResponse> RunSingleAgentAsync(
        IChatClient chatClient,
        string goal,
        CancellationToken cancellationToken,
        Func<string, string?, AnalystOutput?, CancellationToken, Task>? onPhaseChanged)
    {
        _logger.LogInformation("Running MAF single sprint planning agent");

        if (onPhaseChanged is not null)
        {
            await onPhaseChanged(SprintOptimizerPhase.Planner, "Sprint agent is planning…", null, cancellationToken);
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
        SprintWorkflowRequest workflowRequest,
        CancellationToken cancellationToken)
    {
        var selected = workflowRequest.Candidates
            .Take(Math.Clamp(workflowRequest.MaxTasks, 1, 50))
            .ToList();

        var reasoning =
            $"Selected {selected.Count} high/medium tasks for a {workflowRequest.DurationDays}-day sprint " +
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
            Steps = steps,
            Stats = workflowRequest.Stats
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
    public AnalystOutput? AnalystOutput { get; set; }
    public SprintWorkloadStats? Stats { get; set; }
}
