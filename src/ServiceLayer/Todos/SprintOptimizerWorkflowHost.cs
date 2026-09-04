#nullable enable

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fistix.TaskManager.AiLayer.Shared;
using Fistix.TaskManager.Core.DomainModel.Constants;
using Fistix.TaskManager.ViewModel.Commands.Todos;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Fistix.TaskManager.ServiceLayer.Todos;

/// <summary>
/// MAF WorkflowBuilder host for Analyst → Planner. HITL approval stays on the job row.
/// </summary>
public sealed class SprintOptimizerWorkflowHost
{
    private readonly SprintPlanningTools _tools;
    private readonly ILogger<SprintOptimizerWorkflowHost> _logger;

    public SprintOptimizerWorkflowHost(
        SprintPlanningTools tools,
        ILogger<SprintOptimizerWorkflowHost> logger)
    {
        _tools = tools;
        _logger = logger;
    }

    public async Task<(AgentResponse PlannerResponse, AnalystOutput AnalystOutput)> RunSequentialAsync(
        IChatClient chatClient,
        string goal,
        int maxTasks,
        SprintWorkflowRequest workflowRequest,
        string analystInstructions,
        string plannerInstructions,
        CancellationToken cancellationToken,
        Func<string, string?, AnalystOutput?, CancellationToken, Task>? onPhaseChanged = null,
        SprintOptimizerCheckpointDto? resumeFrom = null)
    {
        _logger.LogInformation("Running MAF WorkflowBuilder Analyst → Planner graph");

        AIAgent planner = chatClient.AsAIAgent(
            instructions: plannerInstructions,
            name: "SprintPlanner",
            description: "Proposes a sprint from the Analyst report.",
            tools:
            [
                AIFunctionFactory.Create(_tools.SearchIncompleteTodos),
                AIFunctionFactory.Create(_tools.ProposeSprintPlan)
            ]);

        _tools.Steps.Add(new AgentStepDto
        {
            AgentName = "Workflow",
            ToolName = "maf_workflow_graph",
            Summary = "MAF BuildSequential(Analyst → Planner)"
        });

        if (resumeFrom?.AnalystOutput is not null
            && string.Equals(resumeFrom.CurrentPhase, SprintOptimizerPhase.Planner, StringComparison.OrdinalIgnoreCase))
        {
            foreach (var step in resumeFrom.Steps)
            {
                if (!_tools.Steps.Any(s => s.ToolName == step.ToolName && s.AgentName == step.AgentName))
                {
                    _tools.Steps.Add(step);
                }
            }

            if (onPhaseChanged is not null)
            {
                await onPhaseChanged(
                    SprintOptimizerPhase.Planner,
                    "Resuming planner from checkpoint…",
                    resumeFrom.AnalystOutput,
                    cancellationToken);
            }

            _tools.SetActiveAgentRole("Planner");
            var resumeGoal = BuildPlannerGoal(goal, resumeFrom.AnalystOutput, maxTasks);
            var resumedPlanner = await planner.RunAsync(resumeGoal, cancellationToken: cancellationToken);
            return (resumedPlanner, resumeFrom.AnalystOutput);
        }

        AIAgent analyst = chatClient.AsAIAgent(
            instructions: analystInstructions,
            name: "SprintAnalyst",
            description: "Analyzes incomplete todos and workload for sprint planning.",
            tools:
            [
                AIFunctionFactory.Create(_tools.SearchIncompleteTodos),
                AIFunctionFactory.Create(_tools.GetWorkloadStats),
                AIFunctionFactory.Create(_tools.FindDueSoonTodos)
            ]);

        if (onPhaseChanged is not null)
        {
            await onPhaseChanged(SprintOptimizerPhase.Analyst, "Analyst is reviewing workload…", null, cancellationToken);
        }

        _tools.SetActiveAgentRole("Analyst");
        var workflow = AgentWorkflowBuilder.BuildSequential([analyst, planner]);
        var (runResult, analystResponseText) = await ExecuteWorkflowGraphAsync(workflow, goal, cancellationToken);

        var analystBrief = LlmOutputValidator.ValidateAgentText(
            string.IsNullOrWhiteSpace(analystResponseText)
                ? ExtractAnalystText(runResult)
                : analystResponseText);

        var analystOutput = SprintCapacityCritic.Apply(
            AnalystOutputParser.Parse(
                analystBrief,
                _tools.CandidateExternalIds,
                workflowRequest.Stats),
            workflowRequest.Candidates.ToList(),
            maxTasks,
            workflowRequest.DurationDays);

        if (onPhaseChanged is not null)
        {
            await onPhaseChanged(
                SprintOptimizerPhase.Planner,
                "Planner is selecting tasks for proposal…",
                analystOutput,
                cancellationToken);
        }

        return (runResult, analystOutput);
    }

    private static async Task<(AgentResponse FinalResponse, string? AnalystText)> ExecuteWorkflowGraphAsync(
        Workflow workflow,
        string goal,
        CancellationToken cancellationToken)
    {
        var streamingRun = await InProcessExecution.Default.RunStreamingAsync(
            workflow,
            goal,
            sessionId: null!,
            cancellationToken);

        AgentResponse? analystResponse = null;
        AgentResponse? finalResponse = null;

        try
        {
            await foreach (var evt in streamingRun.WatchStreamAsync(cancellationToken))
            {
                if (evt is not AgentResponseEvent responseEvent)
                {
                    continue;
                }

                if (analystResponse is null)
                {
                    analystResponse = responseEvent.Response;
                }

                finalResponse = responseEvent.Response;
            }
        }
        finally
        {
            await streamingRun.DisposeAsync();
        }

        return (finalResponse ?? analystResponse ?? new AgentResponse(), analystResponse?.Text);
    }

    private static string ExtractAnalystText(AgentResponse response)
    {
        if (!string.IsNullOrWhiteSpace(response.Text))
        {
            return response.Text;
        }

        if (response.Messages is null || response.Messages.Count == 0)
        {
            return "Analyst did not return a summary. Prioritize High priority and earliest due dates.";
        }

        foreach (var message in response.Messages)
        {
            var text = message.Text;
            if (!string.IsNullOrWhiteSpace(text) && text.Contains('{'))
            {
                return text;
            }
        }

        return response.Messages[^1].Text
               ?? "Analyst did not return a summary. Prioritize High priority and earliest due dates.";
    }

    private string BuildPlannerGoal(string goal, AnalystOutput analyst, int maxTasks)
    {
        var idHint = analyst.RecommendedIds.Count == 0
            ? (_tools.CandidateExternalIds.Count == 0
                ? "No candidates loaded."
                : string.Join(", ", _tools.CandidateExternalIds.Take(Math.Min(_tools.CandidateExternalIds.Count, maxTasks * 2))))
            : string.Join(", ", analyst.RecommendedIds.Take(maxTasks));

        var risks = analyst.Risks.Count == 0
            ? "None noted."
            : string.Join("; ", analyst.Risks);

        return $"""
            {goal}

            --- Analyst report ---
            {analyst.Summary}

            Theme: {(string.IsNullOrWhiteSpace(analyst.Theme) ? "n/a" : analyst.Theme)}
            Risks: {risks}

            --- Valid todo external GUIDs (use only these in propose_sprint_plan, max {maxTasks}) ---
            {idHint}

            Call propose_sprint_plan before finishing.
            """;
    }
}
