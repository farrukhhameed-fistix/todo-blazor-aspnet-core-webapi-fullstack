#nullable enable

using Fistix.TaskManager.AiLayer.Abstractions;
using Fistix.TaskManager.AiLayer.Implementations;
using Fistix.TaskManager.AiLayer.Shared;
using Fistix.TaskManager.Core.Abstractions.Repositories;
using Fistix.TaskManager.Core.Abstractions.Services;
using Fistix.TaskManager.Core.DomainModel.Aggregates;
using Fistix.TaskManager.Core.DomainModel.Constants;
using Fistix.TaskManager.Core.DomainModel.SeedWork;
using Fistix.TaskManager.Core.SecurityModel;
using Fistix.TaskManager.ServiceLayer.Knowledge;
using Fistix.TaskManager.ViewModel.Commands.Knowledge;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace Fistix.TaskManager.ServiceLayer.Tests;

/// <summary>
/// Runs the v2-gap manual matrix against sample handbooks using real chunking + BGE vector retrieval.
/// Output is written to test console — run with: dotnet test --filter KnowledgeLabGapAnalysis
/// </summary>
public class KnowledgeLabGapAnalysisTests
{
    private const int ChunkSize = 800;
    private const int ChunkOverlap = 100;
    private const int RetrievalLimit = 5;
    private const double MinSimilarity = 0.45;

    private readonly ITestOutputHelper _output;

    public KnowledgeLabGapAnalysisTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task RunV2GapMatrix_AndPrintResults()
    {
        var repoRoot = FindRepoRoot();
        var stressPath = Path.Combine(repoRoot, "samples/knowledge-lab/v2-gap-stress-handbook.md");
        var acmePath = Path.Combine(repoRoot, "samples/knowledge-lab/acme-sprint-handbook.md");
        Assert.True(File.Exists(stressPath), $"Missing {stressPath}");
        Assert.True(File.Exists(acmePath), $"Missing {acmePath}");

        var stressText = await File.ReadAllTextAsync(stressPath);
        var acmeText = await File.ReadAllTextAsync(acmePath);

        _output.WriteLine("=== Knowledge Lab v1 gap matrix — automated run ===");
        _output.WriteLine($"Config: chunk {ChunkSize}/{ChunkOverlap}, retrieve {RetrievalLimit}, MinSimilarity {MinSimilarity}, vector-only");
        _output.WriteLine(string.Empty);

        // --- A: ingest limits (handler, no DB) ---
        _output.WriteLine("--- A. Ingest & format limits ---");
        await AssertUploadRejects("notes.pdf", "hello", "A1 PDF", "Only .txt and .md");
        await AssertUploadRejects("empty.md", "", "A2 empty", "File is empty");
        var bigContent = new string('x', 2 * 1024 * 1024 + 1);
        await AssertUploadRejects("big.txt", bigContent, "A3 >2MB", "byte upload limit");

        var stressChunks = TextChunker.Split(stressText, ChunkSize, ChunkOverlap);
        var acmeChunks = TextChunker.Split(acmeText, ChunkSize, ChunkOverlap);
        _output.WriteLine($"A5 stress doc chunk count: {stressChunks.Count} (appendix alone >5 → retrieval cap matters)");
        _output.WriteLine(string.Empty);

        // --- E: chunking structure ---
        _output.WriteLine("--- E. Chunking stress ---");
        var tableChunk = stressChunks.FirstOrDefault(c => c.Content.Contains("Cross-squad comparison", StringComparison.Ordinal));
        var tableSplit = tableChunk is null ||
            !tableChunk.Content.Contains("Platform", StringComparison.Ordinal) ||
            !tableChunk.Content.Contains("Payments", StringComparison.Ordinal);
        _output.WriteLine($"E1 comparison table split mid-row: {(tableSplit ? "YES (table may span chunks)" : "NO (whole table in one chunk)")}");
        var appendixChunks = stressChunks.Count(c => c.Content.Contains("Paragraph ", StringComparison.Ordinal));
        _output.WriteLine($"E2 appendix paragraph chunks: {appendixChunks} of {stressChunks.Count} total");
        _output.WriteLine(string.Empty);

        var modelDir = FindModelDirectory(repoRoot);
        if (modelDir is null)
        {
            _output.WriteLine("SKIP vector retrieval: ONNX model not found under models/bge-small-en-v1.5");
            return;
        }

        using var embedder = CreateEmbedder(modelDir);
        var stressEmbeddings = await EmbedChunksAsync(embedder, stressChunks);
        var acmeEmbeddings = await EmbedChunksAsync(embedder, acmeChunks);
        var allEmbeddings = stressEmbeddings.Concat(acmeEmbeddings).ToList();

        _output.WriteLine("--- B. Retrieval (stress doc scoped) ---");
        await RunRetrievalCase(embedder, "B1", "What is EXACT-TOKEN-ALPHA?",
            "acme-staging.us.auth0.com", stressEmbeddings);
        await RunRetrievalCase(embedder, "B2", "What env var holds the Stripe webhook secret?",
            "STRIPE_WHSEC_PAYMENTS", stressEmbeddings);
        await RunRetrievalCase(embedder, "B3", "Compare Platform vs Payments P1 restore times",
            "2 hours", stressEmbeddings, alsoNeed: "4 hours");
        await RunRetrievalCase(embedder, "B4", "Who is on-call for Platform in week B?",
            "Lina", stressEmbeddings);
        await RunRetrievalCase(embedder, "B5", "What mitigated AUTH-INC-9001 in Safari?",
            "refresh-token rotation", stressEmbeddings);
        await RunRetrievalCase(embedder, "B6", "How fast must silent refresh be?",
            "800 ms", stressEmbeddings);
        await RunRetrievalCase(embedder, "B7", "What keyword goes in Auth0 refresh tickets?",
            "SILENT-REFRESH-PLAYBOOK", stressEmbeddings);
        await RunRetrievalCase(embedder, "B8", "Does v1 use hybrid search?",
            "vector-only", stressEmbeddings);

        _output.WriteLine(string.Empty);
        _output.WriteLine("--- C. Cross-document (both docs, no filter) ---");
        await RunRetrievalCase(embedder, "C1", "Who is product owner?",
            "Maya Chen", acmeEmbeddings, corpusLabel: "acme-only expected");
        await RunRetrievalCase(embedder, "C2", "Platform P1 ack vs Payments P1 ack?",
            "15 min", allEmbeddings, alsoNeed: "30 min", corpusLabel: "both docs merged");

        _output.WriteLine(string.Empty);
        _output.WriteLine("--- D. Safety (ground truth must NOT appear — insufficient OK) ---");
        await RunRetrievalCase(embedder, "D1", "What is EXACT-TOKEN-GAMMA used for?",
            "GraphRAG", stressEmbeddings, expectAbsent: true);
        await RunRetrievalCase(embedder, "D2", "What is the Redis vector connection string?",
            "redis://", stressEmbeddings, expectAbsent: true);

        _output.WriteLine(string.Empty);
        _output.WriteLine("=== Done. See Pass/Fail column above ===");
    }

    private async Task RunRetrievalCase(
        OnnxBgeEmbeddingService embedder,
        string id,
        string question,
        string groundTruthSnippet,
        IReadOnlyList<(TextChunk chunk, float[] vector)> embeddings,
        string? alsoNeed = null,
        bool expectAbsent = false,
        string? corpusLabel = null)
    {
        var queryVec = await embedder.GenerateEmbeddingAsync(question, EmbeddingInputKind.Query);

        var ranked = embeddings
            .Select(e => (e.chunk, score: Dot(e.vector, queryVec)))
            .OrderByDescending(x => x.score)
            .ToList();

        var hits = ranked
            .Where(x => x.score >= MinSimilarity)
            .Take(RetrievalLimit)
            .ToList();

        var topText = string.Join(" ", hits.Select(h => h.chunk.Content));
        var hasPrimary = topText.Contains(groundTruthSnippet, StringComparison.OrdinalIgnoreCase);
        var hasSecondary = alsoNeed is null ||
            topText.Contains(alsoNeed, StringComparison.OrdinalIgnoreCase);
        var simRange = hits.Count == 0
            ? "none"
            : $"{hits.Min(h => h.score):F3}–{hits.Max(h => h.score):F3}";

        string verdict;
        if (expectAbsent)
        {
            verdict = hasPrimary ? "FAIL (leaked into context)" : "PASS v1 (not retrieved / LLM should refuse)";
        }
        else if (hasPrimary && hasSecondary)
        {
            verdict = "PASS v1";
        }
        else if (hasPrimary || hasSecondary)
        {
            verdict = "PARTIAL → v2 (multi-chunk / rerank)";
        }
        else
        {
            verdict = ranked.First().score >= MinSimilarity * 0.9
                ? "MISS → v2 (hybrid FTS / rewrite)"
                : "MISS → v2 (hybrid FTS / rewrite)";
        }

        _output.WriteLine(
            $"{id} [{verdict}] Q: {question}" +
            (corpusLabel is null ? "" : $" ({corpusLabel})"));
        _output.WriteLine($"    hits={hits.Count}, sim={simRange}, ground in top-{RetrievalLimit}: primary={hasPrimary}, secondary={hasSecondary}");
        if (hits.Count > 0)
        {
            _output.WriteLine($"    top chunk ordinals: {string.Join(", ", hits.Select(h => h.chunk.Ordinal))}");
        }
    }

    private async Task AssertUploadRejects(string fileName, string content, string label, string expectedFragment)
    {
        var handler = new UploadKnowledgeDocumentCommandHandler(
            new GapFakeDocumentRepository(),
            new GapFakeJobRepository(),
            GapUser(),
            EnabledConfig(),
            NullLogger<UploadKnowledgeDocumentCommandHandler>.Instance);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new UploadKnowledgeDocumentCommand { FileName = fileName, Content = content },
                CancellationToken.None));
        var pass = ex.Message.Contains(expectedFragment, StringComparison.OrdinalIgnoreCase);
        _output.WriteLine($"{label}: {(pass ? "PASS" : "FAIL")} — {ex.Message}");
        Assert.Contains(expectedFragment, ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<List<(TextChunk chunk, float[] vector)>> EmbedChunksAsync(
        OnnxBgeEmbeddingService embedder,
        IReadOnlyList<TextChunk> chunks)
    {
        var result = new List<(TextChunk, float[])>();
        foreach (var chunk in chunks)
        {
            var vec = await embedder.GenerateEmbeddingAsync(chunk.Content, EmbeddingInputKind.Passage);
            result.Add((chunk, vec));
        }

        return result;
    }

    private static OnnxBgeEmbeddingService CreateEmbedder(string modelDir)
    {
        var aiConfig = new AiConfiguration
        {
            Features = new AiFeaturesConfiguration { EnableEmbeddings = true, EnableKnowledgeRag = true },
            Embedding = new EmbeddingSettings
            {
                Provider = "Onnx",
                Model = "bge-small-en-v1.5",
                Dimension = 384,
                Onnx = new OnnxEmbeddingSettings
                {
                    ModelDirectory = modelDir,
                    MaxSequenceLength = 512,
                    QueryInstruction = "Represent this sentence for searching: "
                }
            }
        };
        return new OnnxBgeEmbeddingService(aiConfig, NullLogger<OnnxBgeEmbeddingService>.Instance);
    }

    private static double Dot(float[] a, float[] b)
    {
        double sum = 0;
        for (var i = 0; i < a.Length; i++)
        {
            sum += a[i] * b[i];
        }

        return sum;
    }

    private static string FindRepoRoot()
    {
        var probe = new DirectoryInfo(AppContext.BaseDirectory);
        while (probe is not null)
        {
            if (File.Exists(Path.Combine(probe.FullName, "src", "TaskManager.sln")))
            {
                return probe.FullName;
            }

            probe = probe.Parent;
        }

        throw new InvalidOperationException("Could not locate repo root.");
    }

    private static string? FindModelDirectory(string repoRoot) =>
        File.Exists(Path.Combine(repoRoot, "models", "bge-small-en-v1.5", "model.onnx"))
            ? Path.Combine(repoRoot, "models", "bge-small-en-v1.5")
            : null;

    private static AiConfiguration EnabledConfig() => new()
    {
        Features = new AiFeaturesConfiguration
        {
            EnableEmbeddings = true,
            EnableKnowledgeRag = true,
            KnowledgeRag = new KnowledgeRagConfiguration()
        }
    };

    private static ICurrentUserService GapUser()
    {
        var profile = new UserProfile
        {
            Name = "GapTester",
            EmailAddress = "gap@test.local"
        };
        profile.GenerateNewExternalId();
        return new GapCurrentUserService(profile);
    }

    private sealed class GapCurrentUserService(UserProfile profile) : ICurrentUserService
    {
        public string Email => profile.EmailAddress;
        public bool HasAdminProfile => profile.IsAdmin;
        public UserProfile UserProfile { get; } = profile;
    }

    private sealed class GapFakeDocumentRepository : IKnowledgeDocumentRepository
    {
        public Task CreateAsync(KnowledgeDocument document, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<KnowledgeDocument?> GetByExternalIdAsync(Guid externalId, CancellationToken cancellationToken) =>
            Task.FromResult<KnowledgeDocument?>(null);
        public Task<KnowledgeDocument?> GetByIdAsync(int id, CancellationToken cancellationToken) =>
            Task.FromResult<KnowledgeDocument?>(null);
        public Task<IReadOnlyList<KnowledgeDocument>> ListByOwnerAsync(Guid ownerExternalId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<KnowledgeDocument>>(Array.Empty<KnowledgeDocument>());
        public Task<KnowledgeDocument?> FindByOwnerAndFileNameAsync(
            Guid ownerExternalId,
            string fileName,
            CancellationToken cancellationToken) =>
            Task.FromResult<KnowledgeDocument?>(null);
        public Task UpdateAsync(KnowledgeDocument document, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task DeleteAsync(KnowledgeDocument document, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class GapFakeJobRepository : IKnowledgeIngestJobRepository
    {
        public Task CreateAsync(KnowledgeIngestJob job, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<KnowledgeIngestJob?> GetByExternalIdAsync(Guid externalId, CancellationToken cancellationToken) =>
            Task.FromResult<KnowledgeIngestJob?>(null);
        public Task<KnowledgeIngestJob?> GetLatestByDocumentIdAsync(int documentId, CancellationToken cancellationToken) =>
            Task.FromResult<KnowledgeIngestJob?>(null);
        public Task<IReadOnlyList<KnowledgeIngestJob>> GetRunnableAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<KnowledgeIngestJob>>(Array.Empty<KnowledgeIngestJob>());
        public Task<IReadOnlyList<KnowledgeIngestJob>> GetStaleRunningAsync(TimeSpan staleAfter, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<KnowledgeIngestJob>>(Array.Empty<KnowledgeIngestJob>());
        public Task UpdateAsync(KnowledgeIngestJob job, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
