#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fistix.TaskManager.AiLayer.Agents;
using Fistix.TaskManager.AiLayer.Observability;
using Fistix.TaskManager.AiLayer.Shared;
using Fistix.TaskManager.Core.Abstractions.Repositories;
using Fistix.TaskManager.Core.DomainModel.Aggregates;
using Fistix.TaskManager.ServiceLayer.Todos;
using Fistix.TaskManager.ViewModel.Commands.Todos;
using Fistix.TaskManager.ViewModel.Dtos;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Fistix.TaskManager.ServiceLayer.Tests;

public class SprintOptimizerEvalTests
{
    [Fact]
    public async Task PlanAsync_EmptyInbox_SkipsLlm()
    {
        var ownerId = Guid.NewGuid();
        var todos = new FakeTodoRepository([]);
        var sprints = new FakeSprintRepository([]);
        var tools = new SprintPlanningTools(todos, sprints, NullAiTelemetry.Instance);

        var agent = new SprintOptimizerAgent(
            new AiChatClientFactory(new AiConfiguration(), NullLogger<AiChatClientFactory>.Instance),
            tools,
            new SprintCandidateLoader(todos, sprints),
            new SprintOptimizerWorkflowHost(tools, NullLogger<SprintOptimizerWorkflowHost>.Instance),
            todos,
            new AiConfiguration(),
            NullLogger<SprintOptimizerAgent>.Instance);

        var plan = await agent.PlanAsync(ownerId, 5, 14, null, CancellationToken.None);

        Assert.Empty(plan.SelectedTodos);
        Assert.Contains(plan.Steps, s => s.ToolName == "empty_inbox");
    }

    [Fact]
    public void AnalystOutputParser_FiltersInvalidIds()
    {
        var valid = Guid.NewGuid();
        var output = AnalystOutputParser.Parse(
            $$"""{"recommendedIds":["{{valid}}","{{Guid.NewGuid()}}"],"summary":"ok"}""",
            [valid]);

        Assert.Single(output.RecommendedIds);
        Assert.Equal(valid, output.RecommendedIds[0]);
    }

    [Fact]
    public void CheckpointService_RoundTripsAnalystOutput()
    {
        var service = new SprintOptimizerCheckpointService();
        var id = Guid.NewGuid();
        var checkpoint = service.Create(
            "planner",
            "summary",
            new AnalystOutput { RecommendedIds = [id], Summary = "summary" },
            [new AgentStepDto { AgentName = "Analyst", ToolName = "search", Summary = "done" }],
            toolInvocationCount: 2);

        var json = service.Serialize(checkpoint);
        var restored = service.Deserialize(json);

        Assert.NotNull(restored);
        Assert.Equal("planner", restored!.CurrentPhase);
        Assert.Single(restored.AnalystOutput!.RecommendedIds);
        Assert.Equal(id, restored.AnalystOutput.RecommendedIds[0]);
    }

    private sealed class FakeTodoRepository(List<TodoTask> todos) : ITodoTaskRepository
    {
        public Task<bool> Create(TodoTask todoTask, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task CreateManyAsync(IReadOnlyList<TodoTask> todoTasks, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<bool> Update(TodoTask todoTask, CancellationToken calcellationToken) => Task.FromResult(true);
        public Task<bool> Delete(Guid id, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<int> DeleteByImportTagAsync(Guid ownerExternalId, string importTag, CancellationToken cancellationToken) => Task.FromResult(0);
        public Task<TodoTask> Get(Guid id, CancellationToken cancellationToken) => Task.FromResult(todos.First());
        public Task<List<TodoTask>> GetAll(CancellationToken cancellationToken) => Task.FromResult(todos.ToList());
        public Task<List<TodoTask>> GetByOwner(Guid ownerExternalId, CancellationToken cancellationToken) =>
            Task.FromResult(todos.Where(t => t.CreatedByUserId == ownerExternalId).ToList());
        public Task<List<TodoTask>> GetByOwnerAndImportTagAsync(Guid ownerExternalId, string importTag, CancellationToken cancellationToken) =>
            Task.FromResult(new List<TodoTask>());
        public Task<IReadOnlyList<TodoImportBatchSummary>> GetImportBatchesByOwnerAsync(Guid ownerExternalId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TodoImportBatchSummary>>([]);
    }

    private sealed class FakeSprintRepository(List<Sprint> sprints) : ISprintRepository
    {
        public Task<bool> Create(Sprint sprint, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<Sprint> Get(Guid externalId, CancellationToken cancellationToken) => Task.FromResult(new Sprint());
        public Task<List<Sprint>> GetByOwner(Guid ownerExternalId, CancellationToken cancellationToken) =>
            Task.FromResult(sprints.Where(s => s.CreatedByUserId == ownerExternalId).ToList());
    }
}
