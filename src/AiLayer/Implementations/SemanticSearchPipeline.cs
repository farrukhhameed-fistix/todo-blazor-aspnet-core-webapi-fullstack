#nullable enable

using Fistix.TaskManager.AiLayer.Abstractions;
using Fistix.TaskManager.AiLayer.Models;
using Fistix.TaskManager.AiLayer.Observability;
using Fistix.TaskManager.AiLayer.Shared;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Fistix.TaskManager.AiLayer.Implementations;

public sealed class SemanticSearchPipeline
{
    private readonly IEmbeddingService _embeddingService;
    private readonly IVectorStore _vectorStore;
    private readonly ILexicalTodoSearch _lexicalSearch;
    private readonly AiConfiguration _aiConfig;
    private readonly IAiTelemetry _telemetry;
    private readonly ILogger<SemanticSearchPipeline> _logger;

    public SemanticSearchPipeline(
        IEmbeddingService embeddingService,
        IVectorStore vectorStore,
        ILexicalTodoSearch lexicalSearch,
        AiConfiguration aiConfig,
        ILogger<SemanticSearchPipeline> logger,
        IAiTelemetry? telemetry = null)
    {
        _embeddingService = embeddingService;
        _vectorStore = vectorStore;
        _lexicalSearch = lexicalSearch;
        _aiConfig = aiConfig;
        _logger = logger;
        _telemetry = telemetry ?? NullAiTelemetry.Instance;
    }

    public async Task<SemanticSearchPipelineResult> ExecuteAsync(
        SemanticSearchPipelineRequest request,
        CancellationToken cancellationToken = default)
    {
        using var operation = _telemetry.StartOperation(
            AiTelemetryNames.Features.SemanticSearch,
            model: _embeddingService.ModelName);

        var sw = Stopwatch.StartNew();
        try
        {
            var sanitizedQuery = PromptInputSanitizer.SanitizeAndTruncate(
                request.Query, LlmInputLimits.SemanticSearchQueryMaxLength);
            if (string.IsNullOrWhiteSpace(sanitizedQuery))
            {
                operation.SetOutcome(AiTelemetryNames.Outcomes.Success);
                return new SemanticSearchPipelineResult
                {
                    Hits = Array.Empty<VectorSearchHit>(),
                    ExecutionTimeMs = 0,
                    Model = _embeddingService.ModelName
                };
            }

            if (request.AllowedExternalIds is { Count: 0 })
            {
                operation.SetOutcome(AiTelemetryNames.Outcomes.Success);
                return new SemanticSearchPipelineResult
                {
                    Hits = Array.Empty<VectorSearchHit>(),
                    ExecutionTimeMs = 0,
                    Model = _embeddingService.ModelName
                };
            }

            var semanticCfg = _aiConfig.Features.SemanticSearch ?? new SemanticSearchConfiguration();
            var hybrid = semanticCfg.HybridEnabled;
            var limit = Math.Clamp(request.Limit <= 0 ? 10 : request.Limit, 1, 100);

            IReadOnlyList<VectorSearchHit> resultHits;
            if (hybrid)
            {
                resultHits = await ExecuteHybridAsync(
                    sanitizedQuery,
                    request.OwnerExternalId,
                    request.AllowedExternalIds,
                    limit,
                    semanticCfg,
                    cancellationToken);
            }
            else
            {
                resultHits = await ExecuteVectorOnlyAsync(
                    sanitizedQuery,
                    request.OwnerExternalId,
                    request.AllowedExternalIds,
                    limit,
                    semanticCfg.MinSimilarity,
                    cancellationToken);
            }

            sw.Stop();

            _logger.LogInformation(
                "Semantic search hybrid={Hybrid} returned {Count} hits in {ElapsedMs}ms for model {Model}",
                hybrid,
                resultHits.Count,
                sw.ElapsedMilliseconds,
                _embeddingService.ModelName);

            operation.Activity?.SetTag(AiTelemetryNames.Tags.LatencyMs, sw.ElapsedMilliseconds);
            operation.SetOutcome(AiTelemetryNames.Outcomes.Success);

            return new SemanticSearchPipelineResult
            {
                Hits = resultHits,
                ExecutionTimeMs = sw.ElapsedMilliseconds,
                Model = _embeddingService.ModelName
            };
        }
        catch
        {
            operation.SetOutcome(AiTelemetryNames.Outcomes.Error);
            throw;
        }
    }

    private async Task<IReadOnlyList<VectorSearchHit>> ExecuteVectorOnlyAsync(
        string query,
        Guid? ownerExternalId,
        IReadOnlyCollection<Guid>? allowedExternalIds,
        int limit,
        double minSimilarity,
        CancellationToken cancellationToken)
    {
        var embedding = await _embeddingService.GenerateEmbeddingAsync(
            query,
            EmbeddingInputKind.Query,
            cancellationToken);
        var hits = await _vectorStore.SearchAsync(
            embedding,
            _embeddingService.ModelName,
            ownerExternalId,
            limit,
            cancellationToken,
            allowedExternalIds);

        return FilterByMinSimilarity(hits, minSimilarity);
    }

    private async Task<IReadOnlyList<VectorSearchHit>> ExecuteHybridAsync(
        string query,
        Guid? ownerExternalId,
        IReadOnlyCollection<Guid>? allowedExternalIds,
        int limit,
        SemanticSearchConfiguration cfg,
        CancellationToken cancellationToken)
    {
        var vectorLimit = Math.Clamp(cfg.VectorCandidateLimit, limit, 200);
        var lexicalLimit = Math.Clamp(cfg.LexicalCandidateLimit, limit, 200);

        var embedding = await _embeddingService.GenerateEmbeddingAsync(
            query,
            EmbeddingInputKind.Query,
            cancellationToken);

        // Sequential: both paths use the same scoped DbContext; Task.WhenAll would throw.
        var vectorHits = await _vectorStore.SearchAsync(
            embedding,
            _embeddingService.ModelName,
            ownerExternalId,
            vectorLimit,
            cancellationToken,
            allowedExternalIds);

        var lexicalHits = await _lexicalSearch.SearchAsync(
            query,
            ownerExternalId,
            lexicalLimit,
            allowedExternalIds,
            cancellationToken);

        // Soft-filter very weak vector neighbors before fusion; FTS-only hits still enter via RRF.
        var minSim = cfg.MinSimilarity;
        var strongVector = FilterByMinSimilarity(vectorHits, minSim);
        if (strongVector.Count == 0 && vectorHits.Count > 0)
        {
            // Keep top vector hit so paraphrases aren't wiped when all sit near the threshold.
            strongVector = vectorHits.Take(Math.Min(5, vectorHits.Count)).ToList();
        }

        var fused = HybridSearchFusion.FuseRrf(strongVector, lexicalHits, cfg.RrfK);
        var blended = HybridSearchFusion.BlendAndTakeAsVectorHits(fused, limit);

        _logger.LogInformation(
            "Hybrid fuse vector={VectorCount} lexical={LexicalCount} fused={FusedCount} out={OutCount}",
            strongVector.Count,
            lexicalHits.Count,
            fused.Count,
            blended.Count);

        return blended;
    }

    /// <summary>Drops nearest-neighbor hits that are too weak to be considered relevant.</summary>
    public static IReadOnlyList<VectorSearchHit> FilterByMinSimilarity(
        IReadOnlyList<VectorSearchHit> hits,
        double minSimilarity)
    {
        if (hits.Count == 0)
        {
            return hits;
        }

        var threshold = Math.Clamp(minSimilarity, 0.0, 1.0);
        return hits.Where(h => h.Similarity >= threshold).ToList();
    }
}
