#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fistix.TaskManager.Core.Abstractions.Repositories;
using Fistix.TaskManager.Core.DomainModel.Aggregates;
using Fistix.TaskManager.ServiceLayer.Todos;
using Fistix.TaskManager.ViewModel.Dtos;
using Xunit;

namespace Fistix.TaskManager.ServiceLayer.Tests;

public class SprintCandidateLoaderTests
{
    [Fact]
    public async Task LoadAsync_ExcludesTodosInActiveSprints()
    {
        var ownerId = Guid.NewGuid();
        var inSprint = MakeTodo(ownerId, "High", internalId: 1);
        var available = MakeTodo(ownerId, "High", internalId: 2);

        var loader = new SprintCandidateLoader(
            new FakeTodoRepository([inSprint, available]),
            new FakeSprintRepository(
            [
                new Sprint
                {
                    Name = "Active",
                    StartDate = DateTime.UtcNow.Date.AddDays(-1),
                    EndDate = DateTime.UtcNow.Date.AddDays(7),
                    CreatedByUserId = ownerId,
                    SprintTodos = [new SprintTodo { TodoId = inSprint.Id }]
                }
            ]));

        var request = await loader.LoadAsync(ownerId, maxTasks: 5, durationDays: 14, name: null, CancellationToken.None);

        Assert.Single(request.Candidates);
        Assert.Equal(available.ExternalId, request.Candidates[0].ExternalId);
        Assert.Equal(1, request.Stats.ExcludedInActiveSprint);
    }

    private static TodoTask MakeTodo(Guid ownerId, string priority, int internalId)
    {
        var todo = new TodoTask
        {
            Title = $"Task {internalId}",
            Priority = priority,
            Status = "Pending",
            DueDate = DateTime.UtcNow.Date.AddDays(3),
            CreatedByUserId = ownerId
        };
        todo.GenerateNewExternalId();
        typeof(Core.DomainModel.SeedWork.Entity).GetProperty("Id")!
            .SetValue(todo, internalId);
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
