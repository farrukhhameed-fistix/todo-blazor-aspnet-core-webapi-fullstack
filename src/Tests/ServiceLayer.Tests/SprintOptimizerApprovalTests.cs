#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fistix.TaskManager.Core.Abstractions.Repositories;
using Fistix.TaskManager.Core.DomainModel.Aggregates;
using Fistix.TaskManager.Core.DomainModel.Constants;
using Fistix.TaskManager.ServiceLayer.Todos;
using Fistix.TaskManager.ViewModel.Commands.Todos;
using Fistix.TaskManager.ViewModel.Dtos;
using Xunit;

namespace Fistix.TaskManager.ServiceLayer.Tests;

public class SprintOptimizerApprovalTests
{
    [Fact]
    public async Task ResolveSelectedTodos_RejectsUnknownIds_AndCapsSelection()
    {
        var ownerId = Guid.NewGuid();
        var t1 = MakeTodo(ownerId, "High");
        var t2 = MakeTodo(ownerId, "Medium");

        var persist = new SprintOptimizerPersistService(
            new FakeTodoRepository([t1, t2]),
            new FakeSprintRepository());

        var selected = await persist.ResolveSelectedTodosAsync(
            ownerId,
            [t1.ExternalId, t2.ExternalId, Guid.NewGuid()],
            maxTasks: 1,
            CancellationToken.None);

        Assert.Single(selected);
        Assert.Equal(t1.ExternalId, selected[0].ExternalId);
    }

    [Fact]
    public void BuildProposal_MarksHeuristicFallback()
    {
        var persist = new SprintOptimizerPersistService(
            new FakeTodoRepository([]),
            new FakeSprintRepository());

        var plan = new SprintOptimizationPlan
        {
            Reasoning = "test",
            Steps =
            [
                new AgentStepDto { AgentName = "Heuristic", ToolName = "heuristic_fallback", Summary = "fallback" }
            ]
        };

        var proposal = persist.BuildProposal(plan, usedHeuristicFallback: true);
        Assert.True(proposal.UsedHeuristicFallback);
        Assert.Contains(proposal.Steps, s => s.ToolName == "heuristic_fallback");
    }

    [Fact]
    public void SerializeProposal_RoundTripsThroughMapper()
    {
        var proposal = new SprintOptimizerProposalDto
        {
            Reasoning = "Pick high priority",
            SelectedTasks =
            [
                new SprintTaskSummaryDto { ExternalId = Guid.NewGuid(), Title = "A", Priority = "High", Status = "Pending" }
            ]
        };

        var json = SprintOptimizerJobMapper.SerializeProposal(proposal);
        var restored = SprintOptimizerJobMapper.DeserializeProposal(json);

        Assert.NotNull(restored);
        Assert.Equal("Pick high priority", restored!.Reasoning);
        Assert.Single(restored.SelectedTasks);
    }

    private static TodoTask MakeTodo(Guid ownerId, string priority)
    {
        var todo = new TodoTask
        {
            Title = $"Task {priority}",
            Description = "desc",
            Priority = priority,
            Status = "Pending",
            DueDate = DateTime.UtcNow.Date.AddDays(3),
            CreatedByUserId = ownerId,
            Category = "Dev"
        };
        todo.GenerateNewExternalId();
        return todo;
    }

    private sealed class FakeTodoRepository(List<TodoTask> todos) : ITodoTaskRepository
    {
        public Task<bool> Create(TodoTask todoTask, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task CreateManyAsync(IReadOnlyList<TodoTask> todoTasks, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<bool> Update(TodoTask todoTask, CancellationToken calcellationToken) => Task.FromResult(true);
        public Task<bool> Delete(Guid id, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<int> DeleteByImportTagAsync(Guid ownerExternalId, string importTag, CancellationToken cancellationToken) => Task.FromResult(0);
        public Task<TodoTask> Get(Guid id, CancellationToken cancellationToken) => Task.FromResult(todos.First(t => t.ExternalId == id));
        public Task<List<TodoTask>> GetAll(CancellationToken cancellationToken) => Task.FromResult(todos.ToList());
        public Task<List<TodoTask>> GetByOwner(Guid ownerExternalId, CancellationToken cancellationToken) =>
            Task.FromResult(todos.Where(t => t.CreatedByUserId == ownerExternalId).ToList());
        public Task<List<TodoTask>> GetByOwnerAndImportTagAsync(Guid ownerExternalId, string importTag, CancellationToken cancellationToken) =>
            Task.FromResult(todos.Where(t => t.CreatedByUserId == ownerExternalId && t.ImportTag == importTag).ToList());

        public Task<IReadOnlyList<TodoImportBatchSummary>> GetImportBatchesByOwnerAsync(
            Guid ownerExternalId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TodoImportBatchSummary>>([]);
    }

    private sealed class FakeSprintRepository : ISprintRepository
    {
        public Task<bool> Create(Sprint sprint, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<Sprint> Get(Guid externalId, CancellationToken cancellationToken) => Task.FromResult(new Sprint());
        public Task<List<Sprint>> GetByOwner(Guid ownerExternalId, CancellationToken cancellationToken) =>
            Task.FromResult(new List<Sprint>());
    }
}
