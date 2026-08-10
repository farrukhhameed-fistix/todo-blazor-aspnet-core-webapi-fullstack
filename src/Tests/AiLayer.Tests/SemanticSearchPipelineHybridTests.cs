#nullable enable

using Fistix.TaskManager.AiLayer.Abstractions;
using Fistix.TaskManager.AiLayer.Implementations;
using Fistix.TaskManager.AiLayer.Models;
using Fistix.TaskManager.AiLayer.Shared;
using Microsoft.Extensions.Logging.Abstractions;

namespace Fistix.TaskManager.AiLayer.Tests;

public class SemanticSearchPipelineHybridTests
{
    private sealed class FakeEmbedding : IEmbeddingService
    {
        public string ModelName => "fake";
        public int Dimension => 3;

        public Task<float[]> GenerateEmbeddingAsync(
            string text,
            EmbeddingInputKind kind = EmbeddingInputKind.Passage,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new float[] { 1, 0, 0 });
    }

    private sealed class FakeVectorStore : IVectorStore
    {
        public IReadOnlyCollection<Guid>? LastAllowed { get; private set; }
        public List<VectorSearchHit> Hits { get; set; } = [];

        public Task UpsertTodoEmbeddingAsync(
            int todoId, float[] embedding, string model, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<VectorSearchHit>> SearchAsync(
            float[] queryEmbedding,
            string embeddingModel,
            Guid? ownerExternalId,
            int limit,
            CancellationToken cancellationToken = default,
            IReadOnlyCollection<Guid>? allowedExternalIds = null)
        {
            LastAllowed = allowedExternalIds;
            var filtered = allowedExternalIds is null
                ? Hits
                : Hits.Where(h => allowedExternalIds.Contains(h.TodoExternalId)).ToList();
            return Task.FromResult<IReadOnlyList<VectorSearchHit>>(filtered.Take(limit).ToList());
        }
    }

    private sealed class FakeLexical : ILexicalTodoSearch
    {
        public int CallCount { get; private set; }
        public IReadOnlyCollection<Guid>? LastAllowed { get; private set; }
        public List<LexicalSearchHit> Hits { get; set; } = [];

        public Task<IReadOnlyList<LexicalSearchHit>> SearchAsync(
            string query,
            Guid? ownerExternalId,
            int limit,
            IReadOnlyCollection<Guid>? allowedExternalIds = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastAllowed = allowedExternalIds;
            var filtered = allowedExternalIds is null
                ? Hits
                : Hits.Where(h => allowedExternalIds.Contains(h.TodoExternalId)).ToList();
            return Task.FromResult<IReadOnlyList<LexicalSearchHit>>(filtered.Take(limit).ToList());
        }
    }

    [Fact]
    public async Task HybridOff_DoesNotCallLexical()
    {
        var vector = new FakeVectorStore
        {
            Hits = [new VectorSearchHit(Guid.NewGuid(), 1, 0.9)]
        };
        var lexical = new FakeLexical();
        var pipeline = CreatePipeline(hybrid: false, vector, lexical);

        await pipeline.ExecuteAsync(new SemanticSearchPipelineRequest { Query = "payments", Limit = 5 });

        Assert.Equal(0, lexical.CallCount);
    }

    [Fact]
    public async Task HybridOn_CallsLexicalAndFuses()
    {
        var shared = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var vector = new FakeVectorStore
        {
            Hits =
            [
                new VectorSearchHit(shared, 1, 0.9),
                new VectorSearchHit(Guid.Parse("11111111-1111-1111-1111-111111111111"), 2, 0.8)
            ]
        };
        var lexical = new FakeLexical
        {
            Hits =
            [
                new LexicalSearchHit(shared, 1, 2.0),
                new LexicalSearchHit(Guid.Parse("22222222-2222-2222-2222-222222222222"), 3, 1.0)
            ]
        };
        var pipeline = CreatePipeline(hybrid: true, vector, lexical);

        var result = await pipeline.ExecuteAsync(new SemanticSearchPipelineRequest { Query = "api key", Limit = 3 });

        Assert.Equal(1, lexical.CallCount);
        Assert.Equal(3, result.Hits.Count);
        Assert.Equal(shared, result.Hits[0].TodoExternalId);
    }

    [Fact]
    public async Task AllowedExternalIds_RestrictsBothLanes()
    {
        var allowed = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var other = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var vector = new FakeVectorStore
        {
            Hits =
            [
                new VectorSearchHit(allowed, 1, 0.9),
                new VectorSearchHit(other, 2, 0.95)
            ]
        };
        var lexical = new FakeLexical
        {
            Hits =
            [
                new LexicalSearchHit(other, 2, 5.0),
                new LexicalSearchHit(allowed, 1, 1.0)
            ]
        };
        var pipeline = CreatePipeline(hybrid: true, vector, lexical);

        var result = await pipeline.ExecuteAsync(new SemanticSearchPipelineRequest
        {
            Query = "auth",
            Limit = 10,
            AllowedExternalIds = [allowed]
        });

        Assert.Equal([allowed], vector.LastAllowed);
        Assert.Equal([allowed], lexical.LastAllowed);
        Assert.All(result.Hits, h => Assert.Equal(allowed, h.TodoExternalId));
    }

    private static SemanticSearchPipeline CreatePipeline(bool hybrid, FakeVectorStore vector, FakeLexical lexical)
    {
        var config = new AiConfiguration
        {
            Features = new AiFeaturesConfiguration
            {
                SemanticSearch = new SemanticSearchConfiguration
                {
                    HybridEnabled = hybrid,
                    MinSimilarity = 0.45,
                    VectorCandidateLimit = 40,
                    LexicalCandidateLimit = 40,
                    RrfK = 60
                }
            }
        };

        return new SemanticSearchPipeline(
            new FakeEmbedding(),
            vector,
            lexical,
            config,
            NullLogger<SemanticSearchPipeline>.Instance);
    }
}
