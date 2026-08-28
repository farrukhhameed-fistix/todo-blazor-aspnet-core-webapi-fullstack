#nullable enable

using System.Reflection;
using Fistix.TaskManager.AiLayer.Abstractions;
using Fistix.TaskManager.AiLayer.Shared;
using Fistix.TaskManager.Core.Abstractions.Repositories;
using Fistix.TaskManager.Core.Abstractions.Services;
using Fistix.TaskManager.Core.DomainModel.Aggregates;
using Fistix.TaskManager.Core.DomainModel.Constants;
using Fistix.TaskManager.Core.DomainModel.SeedWork;
using Fistix.TaskManager.Core.Exceptions;
using Fistix.TaskManager.ServiceLayer.Knowledge;
using Fistix.TaskManager.ServiceLayer.Notifications;
using Fistix.TaskManager.ViewModel.Commands.Knowledge;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Fistix.TaskManager.ServiceLayer.Tests;

public class KnowledgeIngestAndQueryTests
{
    [Fact]
    public async Task Upload_RejectsNonTextExtension()
    {
        var (owner, user) = CreateUser();
        var handler = new UploadKnowledgeDocumentCommandHandler(
            new FakeDocumentRepository(),
            new FakeJobRepository(),
            user,
            EnabledConfig(),
            NullLogger<UploadKnowledgeDocumentCommandHandler>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new UploadKnowledgeDocumentCommand
            {
                FileName = "notes.pdf",
                Content = "hello"
            }, CancellationToken.None));
        Assert.Equal(owner, user.UserProfile.ExternalId);
    }

    [Fact]
    public async Task Upload_CreatesPendingDocumentAndJob()
    {
        var (_, user) = CreateUser();
        var docs = new FakeDocumentRepository();
        var jobs = new FakeJobRepository();
        var handler = new UploadKnowledgeDocumentCommandHandler(
            docs,
            jobs,
            user,
            EnabledConfig(),
            NullLogger<UploadKnowledgeDocumentCommandHandler>.Instance);

        var result = await handler.Handle(new UploadKnowledgeDocumentCommand
        {
            FileName = "notes.md",
            ContentType = "text/markdown",
            Content = "# Hello\n\nWorld from Auth0."
        }, CancellationToken.None);

        Assert.Single(docs.Items);
        Assert.Single(jobs.Items);
        Assert.Equal(KnowledgeDocumentStatus.Pending, result.Payload.Document.Status);
        Assert.Equal(KnowledgeIngestStepNames.Parse, result.Payload.Job.CurrentStep);
        Assert.Equal("notes.md", result.Payload.Document.FileName);
    }

    [Fact]
    public async Task Processor_ParseChunkEmbed_MarksReady()
    {
        var document = NewDocument(1, Guid.NewGuid(), "# Title\n\nEnough text to become a chunk about payments and auth.");
        var job = NewJob(document);
        var docs = new FakeDocumentRepository([document]);
        var chunks = new FakeChunkRepository();
        var embeddings = new FakeEmbeddingRepository();
        var jobs = new FakeJobRepository([job]);
        var processor = new KnowledgeIngestProcessor(
            docs,
            chunks,
            embeddings,
            jobs,
            new FakeEmbeddingService(),
            new NullKnowledgeIngestNotifier(),
            EnabledConfig(),
            NullLogger<KnowledgeIngestProcessor>.Instance);

        await processor.ProcessNextStepAsync(job, CancellationToken.None);
        Assert.Equal(KnowledgeIngestStepNames.Chunk, job.CurrentStep);

        await processor.ProcessNextStepAsync(job, CancellationToken.None);
        Assert.Equal(KnowledgeIngestStepNames.Embed, job.CurrentStep);
        Assert.True(document.ChunkCount >= 1);

        await processor.ProcessNextStepAsync(job, CancellationToken.None);
        Assert.Equal(AiBatchJobStatus.Completed, job.Status);
        Assert.Equal(KnowledgeDocumentStatus.Ready, document.Status);
        Assert.Equal(document.ChunkCount, embeddings.Stored.Count);
    }

    [Fact]
    public async Task Query_EmptyHits_InsufficientContext()
    {
        var (_, user) = CreateUser();
        var pipeline = new Fistix.TaskManager.AiLayer.Implementations.RAGPipeline(
            new FakeLlm(),
            EnabledConfig(),
            NullLogger<Fistix.TaskManager.AiLayer.Implementations.RAGPipeline>.Instance);

        var handler = new KnowledgeQueryCommandHandler(
            pipeline,
            new FakeEmbeddingService(),
            new FakeDocumentRepository(),
            new FakeEmbeddingRepository(),
            user,
            EnabledConfig(),
            NullLogger<KnowledgeQueryCommandHandler>.Instance);

        var result = await handler.Handle(new KnowledgeQueryCommand { Question = "What is Auth0?" }, CancellationToken.None);

        Assert.Equal(LlmOutputValidator.InsufficientKnowledgeContextMessage, result.Payload.Answer);
        Assert.Empty(result.Payload.Sources);
        Assert.Equal(0, result.Payload.Trace.HitCount);
    }

    [Fact]
    public async Task Query_DoesNotSearchOtherOwnersHits()
    {
        var (owner, user) = CreateUser();
        var other = Guid.NewGuid();
        var embeddings = new FakeEmbeddingRepository();
        embeddings.Hits.Add(new KnowledgeChunkSearchHit(
            Guid.NewGuid(), 1, Guid.NewGuid(), "other.md", 0, "secret", null, 0.01));
        embeddings.ExpectedOwner = owner;

        var pipeline = new Fistix.TaskManager.AiLayer.Implementations.RAGPipeline(
            new FakeLlm { Response = "ok" },
            EnabledConfig(),
            NullLogger<Fistix.TaskManager.AiLayer.Implementations.RAGPipeline>.Instance);

        var handler = new KnowledgeQueryCommandHandler(
            pipeline,
            new FakeEmbeddingService(),
            new FakeDocumentRepository(),
            embeddings,
            user,
            EnabledConfig(),
            NullLogger<KnowledgeQueryCommandHandler>.Instance);

        await handler.Handle(new KnowledgeQueryCommand { Question = "secret?" }, CancellationToken.None);
        Assert.Equal(owner, embeddings.LastOwner);
        Assert.NotEqual(other, embeddings.LastOwner);
    }

    [Fact]
    public async Task GetDocument_OtherOwner_Forbidden()
    {
        var (_, user) = CreateUser();
        var stranger = NewDocument(2, Guid.NewGuid(), "private");
        stranger.GenerateNewExternalId();
        var docs = new FakeDocumentRepository([stranger]);
        var handler = new GetKnowledgeDocumentQueryHandler(
            docs,
            new FakeJobRepository(),
            user,
            EnabledConfig());

        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            handler.Handle(
                new Fistix.TaskManager.ViewModel.Queries.Knowledge.GetKnowledgeDocumentQuery
                {
                    DocumentExternalId = stranger.ExternalId
                },
                CancellationToken.None));
    }

    private static AiConfiguration EnabledConfig() => new()
    {
        Provider = "ollama",
        Features = new AiFeaturesConfiguration
        {
            EnableKnowledgeRag = true,
            EnableEmbeddings = true,
            KnowledgeRag = new KnowledgeRagConfiguration
            {
                ChunkSize = 80,
                ChunkOverlap = 10,
                RetrievalLimit = 5,
                MinSimilarity = 0.1
            }
        }
    };

    private static (Guid OwnerId, FakeCurrentUserService User) CreateUser()
    {
        var profile = new UserProfile { Name = "Tester", EmailAddress = "t@example.com" };
        profile.GenerateNewExternalId();
        return (profile.ExternalId, new FakeCurrentUserService(profile));
    }

    private static KnowledgeDocument NewDocument(int id, Guid owner, string text)
    {
        var document = new KnowledgeDocument
        {
            CreatedByUserId = owner,
            FileName = "doc.md",
            ContentType = "text/markdown",
            ExtractedText = text,
            Status = KnowledgeDocumentStatus.Pending,
            FileSizeBytes = text.Length
        };
        document.GenerateNewExternalId();
        SetId(document, id);
        return document;
    }

    private static KnowledgeIngestJob NewJob(KnowledgeDocument document)
    {
        var job = new KnowledgeIngestJob
        {
            DocumentId = document.Id,
            CreatedByUserId = document.CreatedByUserId,
            CurrentStep = KnowledgeIngestStepNames.Parse,
            Status = AiBatchJobStatus.Running
        };
        job.GenerateNewExternalId();
        SetId(job, 1);
        return job;
    }

    private static void SetId(Entity entity, int id)
    {
        var property = typeof(Entity).GetProperty(nameof(Entity.Id))!;
        property.GetSetMethod(nonPublic: true)!.Invoke(entity, [id]);
    }

    private sealed class FakeCurrentUserService(UserProfile profile) : ICurrentUserService
    {
        public string Email => profile.EmailAddress;
        public bool HasAdminProfile => profile.IsAdmin;
        public UserProfile UserProfile => profile;
    }

    private sealed class FakeLlm : ILlmProviderService
    {
        public string Response { get; set; } = "grounded";
        public Task<string> GetCompletionAsync(string prompt, CancellationToken cancellationToken = default) =>
            Task.FromResult(Response);
    }

    private sealed class FakeEmbeddingService : IEmbeddingService
    {
        public string ModelName => "test-embed";
        public int Dimension => 384;

        public Task<float[]> GenerateEmbeddingAsync(
            string text,
            EmbeddingInputKind kind = EmbeddingInputKind.Passage,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new float[8]);
    }

    private sealed class FakeDocumentRepository : IKnowledgeDocumentRepository
    {
        public FakeDocumentRepository() : this([]) { }
        public FakeDocumentRepository(List<KnowledgeDocument> items) => Items = items;
        public List<KnowledgeDocument> Items { get; }
        private int _nextId = 10;

        public Task CreateAsync(KnowledgeDocument document, CancellationToken cancellationToken)
        {
            if (document.Id == 0)
            {
                SetId(document, _nextId++);
            }

            Items.Add(document);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(KnowledgeDocument document, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<KnowledgeDocument?> GetByExternalIdAsync(Guid externalId, CancellationToken cancellationToken) =>
            Task.FromResult(Items.FirstOrDefault(d => d.ExternalId == externalId));

        public Task<KnowledgeDocument?> GetByIdAsync(int id, CancellationToken cancellationToken) =>
            Task.FromResult(Items.FirstOrDefault(d => d.Id == id));

        public Task<IReadOnlyList<KnowledgeDocument>> ListByOwnerAsync(Guid ownerExternalId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<KnowledgeDocument>>(Items.Where(d => d.CreatedByUserId == ownerExternalId).ToList());

        public Task DeleteAsync(KnowledgeDocument document, CancellationToken cancellationToken)
        {
            Items.Remove(document);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeChunkRepository : IKnowledgeChunkRepository
    {
        public List<KnowledgeChunk> Items { get; } = [];
        private int _nextId = 1;

        public Task ReplaceChunksAsync(int documentId, IReadOnlyList<KnowledgeChunk> chunks, CancellationToken cancellationToken)
        {
            Items.RemoveAll(c => c.DocumentId == documentId);
            foreach (var chunk in chunks)
            {
                chunk.DocumentId = documentId;
                if (chunk.Id == 0)
                {
                    SetId(chunk, _nextId++);
                }

                Items.Add(chunk);
            }

            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<KnowledgeChunk>> GetByDocumentIdAsync(int documentId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<KnowledgeChunk>>(Items.Where(c => c.DocumentId == documentId).OrderBy(c => c.Ordinal).ToList());

        public Task<IReadOnlyList<KnowledgeChunk>> GetByExternalIdsAsync(
            IReadOnlyCollection<Guid> externalIds,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<KnowledgeChunk>>(Items.Where(c => externalIds.Contains(c.ExternalId)).ToList());
    }

    private sealed class FakeEmbeddingRepository : IKnowledgeChunkEmbeddingRepository
    {
        public List<int> Stored { get; } = [];
        public List<KnowledgeChunkSearchHit> Hits { get; } = [];
        public Guid? ExpectedOwner { get; set; }
        public Guid LastOwner { get; private set; }

        public Task UpsertAsync(int chunkId, float[] embedding, string model, CancellationToken cancellationToken)
        {
            Stored.Add(chunkId);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<KnowledgeChunkSearchHit>> SearchSimilarAsync(
            float[] queryEmbedding,
            string embeddingModel,
            Guid ownerExternalId,
            int limit,
            CancellationToken cancellationToken,
            Guid? documentExternalId = null)
        {
            LastOwner = ownerExternalId;
            var owned = Hits.Where(_ => ExpectedOwner is null || ownerExternalId == ExpectedOwner).Take(limit).ToList();
            return Task.FromResult<IReadOnlyList<KnowledgeChunkSearchHit>>(owned);
        }
    }

    private sealed class FakeJobRepository : IKnowledgeIngestJobRepository
    {
        public FakeJobRepository() : this([]) { }
        public FakeJobRepository(List<KnowledgeIngestJob> items) => Items = items;
        public List<KnowledgeIngestJob> Items { get; }

        public Task CreateAsync(KnowledgeIngestJob job, CancellationToken cancellationToken)
        {
            Items.Add(job);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(KnowledgeIngestJob job, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<KnowledgeIngestJob?> GetByExternalIdAsync(Guid externalId, CancellationToken cancellationToken) =>
            Task.FromResult(Items.FirstOrDefault(j => j.ExternalId == externalId));

        public Task<KnowledgeIngestJob?> GetLatestByDocumentIdAsync(int documentId, CancellationToken cancellationToken) =>
            Task.FromResult(Items.Where(j => j.DocumentId == documentId).OrderByDescending(j => j.CreatedAt).FirstOrDefault());

        public Task<IReadOnlyList<KnowledgeIngestJob>> GetRunnableAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<KnowledgeIngestJob>>(Items);

        public Task<IReadOnlyList<KnowledgeIngestJob>> GetStaleRunningAsync(TimeSpan staleAfter, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<KnowledgeIngestJob>>([]);
    }
}
