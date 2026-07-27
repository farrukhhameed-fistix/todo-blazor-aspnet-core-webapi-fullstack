#nullable enable

using Fistix.TaskManager.Core.Abstractions.Repositories;
using Fistix.TaskManager.Core.Abstractions.Services;
using Fistix.TaskManager.Core.DomainModel.Aggregates;
using Fistix.TaskManager.Core.DomainModel.Constants;
using Fistix.TaskManager.Core.Exceptions;
using Fistix.TaskManager.ServiceLayer.Notifications;
using Fistix.TaskManager.ServiceLayer.Todos;
using Fistix.TaskManager.ViewModel.Commands.Todos;
using Fistix.TaskManager.ViewModel.Queries.Todos;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Fistix.TaskManager.ServiceLayer.Tests;

public class CsvImportAndAiBatchHandlerTests
{
    private const string SampleCsv = """
        Title,Description,DueDate,Status,Priority,Category
        Task A,Desc A,2026-08-01,Pending,High,Auth
        Task B,Desc B,2026-08-02,Pending,Medium,API
        """;

    [Fact]
    public async Task Import_PersistsTodos_WithTag_AndNoAiMetadataCreated()
    {
        var (owner, currentUser) = CreateUser();
        var todos = new FakeTodoRepository();
        var handler = new ImportTodoTasksFromCsvCommandHandler(
            todos,
            currentUser,
            NullLogger<ImportTodoTasksFromCsvCommandHandler>.Instance);

        var result = await handler.Handle(new ImportTodoTasksFromCsvCommand
        {
            CsvContent = SampleCsv,
            ImportTag = "import-a",
            DryRun = false
        }, CancellationToken.None);

        Assert.Equal(2, result.Payload.ImportedCount);
        Assert.Equal("import-a", result.Payload.ImportTag);
        Assert.Equal(2, result.Payload.TodoExternalIds.Count);
        Assert.Equal(2, todos.Items.Count);
        Assert.All(todos.Items, t =>
        {
            Assert.Equal(owner, t.CreatedByUserId);
            Assert.Equal("import-a", t.ImportTag);
            Assert.False(string.IsNullOrWhiteSpace(t.Title));
        });
    }

    [Fact]
    public async Task Import_DryRun_DoesNotPersist()
    {
        var (_, currentUser) = CreateUser();
        var todos = new FakeTodoRepository();
        var handler = new ImportTodoTasksFromCsvCommandHandler(
            todos,
            currentUser,
            NullLogger<ImportTodoTasksFromCsvCommandHandler>.Instance);

        var result = await handler.Handle(new ImportTodoTasksFromCsvCommand
        {
            CsvContent = SampleCsv,
            ImportTag = "dry",
            DryRun = true
        }, CancellationToken.None);

        Assert.Equal(2, result.Payload.ImportedCount);
        Assert.True(result.Payload.DryRun);
        Assert.Empty(todos.Items);
        Assert.Empty(result.Payload.TodoExternalIds);
    }

    [Fact]
    public async Task Import_ReplaceExistingTag_DeletesThenInserts()
    {
        var (owner, currentUser) = CreateUser();
        var existing = MakeTodo(owner, "import-a", "Old");
        var todos = new FakeTodoRepository([existing]);
        var handler = new ImportTodoTasksFromCsvCommandHandler(
            todos,
            currentUser,
            NullLogger<ImportTodoTasksFromCsvCommandHandler>.Instance);

        var result = await handler.Handle(new ImportTodoTasksFromCsvCommand
        {
            CsvContent = SampleCsv,
            ImportTag = "import-a",
            ReplaceExistingTag = true
        }, CancellationToken.None);

        Assert.Equal(2, result.Payload.ImportedCount);
        Assert.Equal(2, todos.Items.Count);
        Assert.DoesNotContain(todos.Items, t => t.Title == "Old");
        Assert.All(todos.Items, t => Assert.Equal("import-a", t.ImportTag));
    }

    [Fact]
    public async Task Import_GeneratesTag_WhenMissing()
    {
        var (_, currentUser) = CreateUser();
        var todos = new FakeTodoRepository();
        var handler = new ImportTodoTasksFromCsvCommandHandler(
            todos,
            currentUser,
            NullLogger<ImportTodoTasksFromCsvCommandHandler>.Instance);

        var result = await handler.Handle(new ImportTodoTasksFromCsvCommand
        {
            CsvContent = SampleCsv
        }, CancellationToken.None);

        Assert.StartsWith("csv-", result.Payload.ImportTag);
        Assert.Equal(2, todos.Items.Count);
    }

    [Fact]
    public async Task DeleteImported_RemovesByTag()
    {
        var (owner, currentUser) = CreateUser();
        var todos = new FakeTodoRepository(
        [
            MakeTodo(owner, "tag-1", "A"),
            MakeTodo(owner, "tag-1", "B"),
            MakeTodo(owner, "tag-2", "C")
        ]);
        var handler = new DeleteImportedTodosCommandHandler(todos, currentUser);

        var result = await handler.Handle(
            new DeleteImportedTodosCommand { ImportTag = "tag-1" },
            CancellationToken.None);

        Assert.Equal(2, result.Payload.DeletedCount);
        Assert.Single(todos.Items);
        Assert.Equal("tag-2", todos.Items[0].ImportTag);
    }

    [Fact]
    public async Task GetImportBatches_MapsAiStatus()
    {
        var (owner, currentUser) = CreateUser();
        var todos = new FakeTodoRepository
        {
            BatchSummaries =
            [
                new TodoImportBatchSummary("not-started", 2, DateTime.UtcNow, DateTime.UtcNow, 2, 2, 2),
                new TodoImportBatchSummary("partial", 2, DateTime.UtcNow, DateTime.UtcNow, 1, 0, 2),
                new TodoImportBatchSummary("complete", 2, DateTime.UtcNow, DateTime.UtcNow, 0, 0, 0)
            ]
        };
        var handler = new GetTodoImportBatchesQueryHandler(todos, currentUser);

        var result = await handler.Handle(new GetTodoImportBatchesQuery(), CancellationToken.None);

        Assert.Equal(3, result.Payload.Count);
        Assert.Equal("NotStarted", result.Payload.Single(b => b.ImportTag == "not-started").AiStatus);
        Assert.Equal("Partial", result.Payload.Single(b => b.ImportTag == "partial").AiStatus);
        Assert.Equal("Complete", result.Payload.Single(b => b.ImportTag == "complete").AiStatus);
    }

    [Fact]
    public async Task StartBatch_CreatesRunningJob_ForImportTag()
    {
        var (owner, currentUser) = CreateUser();
        var todos = new FakeTodoRepository(
        [
            MakeTodo(owner, "batch-1", "A"),
            MakeTodo(owner, "batch-1", "B")
        ]);
        var jobs = new FakeAiBatchJobRepository();
        var handler = new StartAiBatchJobCommandHandler(
            jobs,
            todos,
            currentUser,
            NullLogger<StartAiBatchJobCommandHandler>.Instance);

        var result = await handler.Handle(new StartAiBatchJobCommand
        {
            ImportTag = "batch-1",
            BatchSize = 5,
            DelayMsBetweenItems = 100,
            Steps = [AiBatchStepNames.Embedding, AiBatchStepNames.Classify]
        }, CancellationToken.None);

        Assert.Equal(AiBatchJobStatus.Running, result.Payload.Status);
        Assert.Equal(2, result.Payload.Total);
        Assert.Equal("batch-1", result.Payload.ImportTag);
        Assert.Equal(2, result.Payload.Steps.Count);
        Assert.Single(jobs.Items);
    }

    [Fact]
    public async Task StartBatch_Throws_WhenNoTodos()
    {
        var (_, currentUser) = CreateUser();
        var handler = new StartAiBatchJobCommandHandler(
            new FakeAiBatchJobRepository(),
            new FakeTodoRepository(),
            currentUser,
            NullLogger<StartAiBatchJobCommandHandler>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new StartAiBatchJobCommand { ImportTag = "missing" }, CancellationToken.None));
    }

    [Fact]
    public async Task StartBatch_Throws_WhenActiveJobExists()
    {
        var (owner, currentUser) = CreateUser();
        var todos = new FakeTodoRepository([MakeTodo(owner, "t1", "A")]);
        var active = new AiBatchJob
        {
            CreatedByUserId = owner,
            Status = AiBatchJobStatus.Running,
            StepsCsv = AiBatchStepNames.Embedding,
            CurrentStep = AiBatchStepNames.Embedding,
            TodoExternalIdsJson = "[]",
            Total = 1
        };
        active.GenerateNewExternalId();
        var jobs = new FakeAiBatchJobRepository([active]);
        var handler = new StartAiBatchJobCommandHandler(
            jobs,
            todos,
            currentUser,
            NullLogger<StartAiBatchJobCommandHandler>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new StartAiBatchJobCommand { ImportTag = "t1" }, CancellationToken.None));
    }

    [Fact]
    public async Task PauseContinueCancel_UpdateStatus()
    {
        var (owner, currentUser) = CreateUser();
        var job = new AiBatchJob
        {
            CreatedByUserId = owner,
            Status = AiBatchJobStatus.Running,
            StepsCsv = AiBatchStepNames.Embedding,
            CurrentStep = AiBatchStepNames.Embedding,
            TodoExternalIdsJson = "[]",
            Total = 1
        };
        job.GenerateNewExternalId();
        var jobs = new FakeAiBatchJobRepository([job]);
        var notifier = new NullAiBatchNotifier();

        var paused = await new PauseAiBatchJobCommandHandler(jobs, currentUser, notifier)
            .Handle(new PauseAiBatchJobCommand { JobExternalId = job.ExternalId }, CancellationToken.None);
        Assert.Equal(AiBatchJobStatus.Paused, paused.Payload.Status);

        var continued = await new ContinueAiBatchJobCommandHandler(jobs, currentUser, notifier)
            .Handle(new ContinueAiBatchJobCommand { JobExternalId = job.ExternalId }, CancellationToken.None);
        Assert.Equal(AiBatchJobStatus.Running, continued.Payload.Status);

        var cancelled = await new CancelAiBatchJobCommandHandler(jobs, currentUser, notifier)
            .Handle(new CancelAiBatchJobCommand { JobExternalId = job.ExternalId }, CancellationToken.None);
        Assert.Equal(AiBatchJobStatus.Cancelled, cancelled.Payload.Status);
        Assert.True(jobs.Items[0].CancelRequested);
    }

    [Fact]
    public async Task Pause_Throws_WhenCompleted()
    {
        var (owner, currentUser) = CreateUser();
        var job = new AiBatchJob
        {
            CreatedByUserId = owner,
            Status = AiBatchJobStatus.Completed,
            StepsCsv = AiBatchStepNames.Embedding,
            CurrentStep = AiBatchStepNames.Embedding,
            TodoExternalIdsJson = "[]",
            Total = 1
        };
        job.GenerateNewExternalId();
        var jobs = new FakeAiBatchJobRepository([job]);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new PauseAiBatchJobCommandHandler(jobs, currentUser, new NullAiBatchNotifier())
                .Handle(new PauseAiBatchJobCommand { JobExternalId = job.ExternalId }, CancellationToken.None));
    }

    [Fact]
    public async Task GetActiveBatch_ReturnsJob_OrNull()
    {
        var (owner, currentUser) = CreateUser();
        var job = new AiBatchJob
        {
            CreatedByUserId = owner,
            Status = AiBatchJobStatus.Paused,
            StepsCsv = AiBatchStepNames.Embedding,
            CurrentStep = AiBatchStepNames.Embedding,
            TodoExternalIdsJson = "[]",
            Total = 1
        };
        job.GenerateNewExternalId();
        var jobs = new FakeAiBatchJobRepository([job]);
        var handler = new GetActiveAiBatchJobQueryHandler(jobs, currentUser);

        var active = await handler.Handle(new GetActiveAiBatchJobQuery(), CancellationToken.None);
        Assert.NotNull(active.Payload);
        Assert.Equal(job.ExternalId, active.Payload!.ExternalId);

        jobs.Items.Clear();
        var none = await handler.Handle(new GetActiveAiBatchJobQuery(), CancellationToken.None);
        Assert.Null(none.Payload);
    }

    [Fact]
    public async Task GetClassification_WithoutMetadata_ReturnsNone()
    {
        var (owner, currentUser) = CreateUser();
        var todo = MakeTodo(owner, null, "No AI");
        var todos = new FakeTodoRepository([todo]);
        var metadata = new FakeTodoAiMetadataRepository();
        var handler = new GetTaskClassificationQueryHandler(todos, metadata, currentUser);

        var result = await handler.Handle(
            new GetTaskClassificationQuery { TodoExternalId = todo.ExternalId },
            CancellationToken.None);

        Assert.Equal(ClassificationStatus.None, result.Payload.Status);
        Assert.Equal(todo.ExternalId, result.Payload.TodoExternalId);
    }

    [Fact]
    public async Task GetClassification_WithMetadata_MapsStatus()
    {
        var (owner, currentUser) = CreateUser();
        var todo = MakeTodo(owner, null, "Has AI");
        var todos = new FakeTodoRepository([todo]);
        var metadata = new FakeTodoAiMetadataRepository
        {
            ByTodoExternalId =
            {
                [todo.ExternalId] = new TodoAiMetadata
                {
                    AiPriority = "HIGH",
                    ConfidenceScore = 0.9f,
                    ClassificationStatus = ClassificationStatus.Completed,
                    AiPriorityReason = "Urgent"
                }
            }
        };
        var handler = new GetTaskClassificationQueryHandler(todos, metadata, currentUser);

        var result = await handler.Handle(
            new GetTaskClassificationQuery { TodoExternalId = todo.ExternalId },
            CancellationToken.None);

        Assert.Equal(ClassificationStatus.Completed, result.Payload.Status);
        Assert.Equal("HIGH", result.Payload.SuggestedPriority);
        Assert.True(result.Payload.FromCache);
    }

    [Fact]
    public async Task Cancel_OtherUsersJob_ThrowsForbidden()
    {
        var (_, currentUser) = CreateUser();
        var job = new AiBatchJob
        {
            CreatedByUserId = Guid.NewGuid(),
            Status = AiBatchJobStatus.Running,
            StepsCsv = AiBatchStepNames.Embedding,
            CurrentStep = AiBatchStepNames.Embedding,
            TodoExternalIdsJson = "[]",
            Total = 1
        };
        job.GenerateNewExternalId();
        var jobs = new FakeAiBatchJobRepository([job]);

        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            new CancelAiBatchJobCommandHandler(jobs, currentUser, new NullAiBatchNotifier())
                .Handle(new CancelAiBatchJobCommand { JobExternalId = job.ExternalId }, CancellationToken.None));
    }

    private static (Guid OwnerId, FakeCurrentUserService User) CreateUser()
    {
        var profile = new UserProfile { Name = "Tester", EmailAddress = "t@example.com" };
        profile.GenerateNewExternalId();
        return (profile.ExternalId, new FakeCurrentUserService(profile));
    }

    private static TodoTask MakeTodo(Guid ownerId, string? importTag, string title)
    {
        var todo = new TodoTask
        {
            Title = title,
            Description = "desc",
            Priority = "Medium",
            Status = "Pending",
            DueDate = DateTime.UtcNow.Date.AddDays(3),
            CreatedByUserId = ownerId,
            CreatedOn = DateTime.UtcNow,
            ImportTag = importTag,
            Category = "Dev"
        };
        todo.GenerateNewExternalId();
        return todo;
    }

    private sealed class FakeCurrentUserService(UserProfile profile) : ICurrentUserService
    {
        public string Email => profile.EmailAddress;
        public bool HasAdminProfile => profile.IsAdmin;
        public UserProfile UserProfile => profile;
    }

    private sealed class FakeTodoRepository : ITodoTaskRepository
    {
        public FakeTodoRepository() : this([])
        {
        }

        public FakeTodoRepository(List<TodoTask> items)
        {
            Items = items;
        }

        public List<TodoTask> Items { get; }
        public List<TodoImportBatchSummary>? BatchSummaries { get; set; }

        public Task<bool> Create(TodoTask todoTask, CancellationToken cancellationToken)
        {
            Items.Add(todoTask);
            return Task.FromResult(true);
        }

        public Task CreateManyAsync(IReadOnlyList<TodoTask> todoTasks, CancellationToken cancellationToken)
        {
            Items.AddRange(todoTasks);
            return Task.CompletedTask;
        }

        public Task<bool> Update(TodoTask todoTask, CancellationToken calcellationToken) =>
            Task.FromResult(true);

        public Task<bool> Delete(Guid id, CancellationToken cancellationToken)
        {
            Items.RemoveAll(t => t.ExternalId == id);
            return Task.FromResult(true);
        }

        public Task<int> DeleteByImportTagAsync(Guid ownerExternalId, string importTag, CancellationToken cancellationToken)
        {
            var removed = Items.RemoveAll(t =>
                t.CreatedByUserId == ownerExternalId
                && string.Equals(t.ImportTag, importTag, StringComparison.Ordinal));
            return Task.FromResult(removed);
        }

        public Task<TodoTask> Get(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Items.First(t => t.ExternalId == id));

        public Task<List<TodoTask>> GetAll(CancellationToken cancellationToken) =>
            Task.FromResult(Items.ToList());

        public Task<List<TodoTask>> GetByOwner(Guid ownerExternalId, CancellationToken cancellationToken) =>
            Task.FromResult(Items.Where(t => t.CreatedByUserId == ownerExternalId).ToList());

        public Task<List<TodoTask>> GetByOwnerAndImportTagAsync(
            Guid ownerExternalId,
            string importTag,
            CancellationToken cancellationToken) =>
            Task.FromResult(Items
                .Where(t => t.CreatedByUserId == ownerExternalId
                            && string.Equals(t.ImportTag, importTag, StringComparison.Ordinal))
                .ToList());

        public Task<IReadOnlyList<TodoImportBatchSummary>> GetImportBatchesByOwnerAsync(
            Guid ownerExternalId,
            CancellationToken cancellationToken)
        {
            if (BatchSummaries is not null)
            {
                return Task.FromResult<IReadOnlyList<TodoImportBatchSummary>>(BatchSummaries);
            }

            var summaries = Items
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

    private sealed class FakeAiBatchJobRepository : IAiBatchJobRepository
    {
        public FakeAiBatchJobRepository() : this([])
        {
        }

        public FakeAiBatchJobRepository(List<AiBatchJob> items)
        {
            Items = items;
        }

        public List<AiBatchJob> Items { get; }

        public Task CreateAsync(AiBatchJob job, CancellationToken cancellationToken)
        {
            Items.Add(job);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(AiBatchJob job, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<AiBatchJob?> GetByExternalIdAsync(Guid externalId, CancellationToken cancellationToken) =>
            Task.FromResult(Items.FirstOrDefault(j => j.ExternalId == externalId));

        public Task<AiBatchJob?> GetActiveByOwnerAsync(Guid ownerExternalId, CancellationToken cancellationToken)
        {
            var active = Items.FirstOrDefault(j =>
                j.CreatedByUserId == ownerExternalId
                && j.Status is AiBatchJobStatus.Pending
                    or AiBatchJobStatus.Running
                    or AiBatchJobStatus.Paused
                    or AiBatchJobStatus.Stuck);
            return Task.FromResult(active);
        }

        public Task<IReadOnlyList<AiBatchJob>> GetRunnableAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AiBatchJob>>(
                Items.Where(j => j.Status == AiBatchJobStatus.Running).ToList());

        public Task<IReadOnlyList<AiBatchJob>> GetStaleRunningAsync(TimeSpan staleAfter, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AiBatchJob>>([]);
    }

    private sealed class FakeTodoAiMetadataRepository : ITodoAiMetadataRepository
    {
        public Dictionary<Guid, TodoAiMetadata> ByTodoExternalId { get; } = new();

        public Task<TodoAiMetadata?> GetByTodoExternalIdAsync(Guid todoExternalId, CancellationToken cancellationToken) =>
            Task.FromResult(ByTodoExternalId.TryGetValue(todoExternalId, out var m) ? m : null);

        public Task UpsertSummaryAsync(int todoId, string summary, string model, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task MarkClassificationPendingAsync(int todoId, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task UpsertClassificationAsync(
            int todoId,
            string priority,
            float confidence,
            string? reason,
            string model,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task MarkClassificationFailedAsync(int todoId, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task SetWasOverriddenAsync(int todoId, bool wasOverridden, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
