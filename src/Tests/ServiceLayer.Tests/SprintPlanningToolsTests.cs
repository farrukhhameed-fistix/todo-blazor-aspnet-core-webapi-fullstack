#nullable enable

using Fistix.TaskManager.Core.Abstractions.Repositories;
using Fistix.TaskManager.Core.DomainModel.Aggregates;
using Fistix.TaskManager.ServiceLayer.Todos;
using Xunit;

namespace Fistix.TaskManager.ServiceLayer.Tests;

public class SprintPlanningToolsTests
{
    [Fact]
    public async Task ProposeSprintPlan_RejectsUnknownIds_AndCapsSelection()
    {
        var ownerId = Guid.NewGuid();
        var t1 = MakeTodo(ownerId, "High");
        var t2 = MakeTodo(ownerId, "Medium");

        var todos = new FakeTodoRepository([t1, t2]);
        var sprints = new FakeSprintRepository();
        var tools = new SprintPlanningTools(todos, sprints);
        await tools.ConfigureAsync(ownerId, maxTasks: 1, durationDays: 14, name: null, multiAgent: true, CancellationToken.None);

        var json = tools.ProposeSprintPlan(
            $"{t1.ExternalId},{t2.ExternalId},{Guid.NewGuid()}",
            "focus on high priority");

        Assert.Contains("\"acceptedCount\":1", json);
        Assert.Single(tools.SelectedTodos);
        Assert.Equal(t1.ExternalId, tools.SelectedTodos[0].ExternalId);
        Assert.Contains(tools.Steps, s => s.ToolName == "propose_sprint_plan" && s.AgentName == "Planner");
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
        public Task<bool> Create(TodoTask todoTask, CancellationToken cancellationToken) =>
            Task.FromResult(true);

        public Task CreateManyAsync(IReadOnlyList<TodoTask> todoTasks, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<bool> Update(TodoTask todoTask, CancellationToken calcellationToken) =>
            Task.FromResult(true);

        public Task<bool> Delete(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(true);

        public Task<int> DeleteByImportTagAsync(Guid ownerExternalId, string importTag, CancellationToken cancellationToken) =>
            Task.FromResult(0);

        public Task<TodoTask> Get(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(todos.First(t => t.ExternalId == id));

        public Task<List<TodoTask>> GetAll(CancellationToken cancellationToken) =>
            Task.FromResult(todos.ToList());

        public Task<List<TodoTask>> GetByOwner(Guid ownerExternalId, CancellationToken cancellationToken) =>
            Task.FromResult(todos.Where(t => t.CreatedByUserId == ownerExternalId).ToList());

        public Task<List<TodoTask>> GetByOwnerAndImportTagAsync(
            Guid ownerExternalId,
            string importTag,
            CancellationToken cancellationToken) =>
            Task.FromResult(todos.Where(t => t.CreatedByUserId == ownerExternalId && t.ImportTag == importTag).ToList());

        public Task<IReadOnlyList<TodoImportBatchSummary>> GetImportBatchesByOwnerAsync(
            Guid ownerExternalId,
            CancellationToken cancellationToken)
        {
            var summaries = todos
                .Where(t => t.CreatedByUserId == ownerExternalId && !string.IsNullOrWhiteSpace(t.ImportTag))
                .GroupBy(t => t.ImportTag!)
                .Select(g => new TodoImportBatchSummary(
                    g.Key,
                    g.Count(),
                    g.Min(x => x.CreatedOn),
                    g.Max(x => x.CreatedOn),
                    g.Count(),
                    g.Count(),
                    g.Count()))
                .OrderByDescending(s => s.NewestCreatedOn)
                .ToList();
            return Task.FromResult<IReadOnlyList<TodoImportBatchSummary>>(summaries);
        }
    }

    private sealed class FakeSprintRepository : ISprintRepository
    {
        public Task<bool> Create(Sprint sprint, CancellationToken cancellationToken) =>
            Task.FromResult(true);

        public Task<Sprint> Get(Guid externalId, CancellationToken cancellationToken) =>
            Task.FromResult(new Sprint());

        public Task<List<Sprint>> GetByOwner(Guid ownerExternalId, CancellationToken cancellationToken) =>
            Task.FromResult(new List<Sprint>());
    }
}
