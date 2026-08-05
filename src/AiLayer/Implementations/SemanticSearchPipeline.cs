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
    private readonly AiConfiguration _aiConfig;
    private readonly IAiTelemetry _telemetry;
    private readonly ILogger<SemanticSearchPipeline> _logger;

    public SemanticSearchPipeline(
        IEmbeddingService embeddingService,
        IVectorStore vectorStore,
        AiConfiguration aiConfig,
        ILogger<SemanticSearchPipeline> logger,
        IAiTelemetry? telemetry = null)
    {
        _embeddingService = embeddingService;
        _vectorStore = vectorStore;
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
            var embedding = await _embeddingService.GenerateEmbeddingAsync(
                request.Query,
                EmbeddingInputKind.Query,
                cancellationToken);
            var hits = await _vectorStore.SearchAsync(
                embedding,
                _embeddingService.ModelName,
                request.OwnerExternalId,
                request.Limit,
                cancellationToken);

            var minSimilarity = _aiConfig.Features.SemanticSearch?.MinSimilarity ?? 0.45;
            var filtered = FilterByMinSimilarity(hits, minSimilarity);
            sw.Stop();

            _logger.LogInformation(
                "Semantic search returned {Count}/{RawCount} hits (minSimilarity={MinSimilarity}) in {ElapsedMs}ms for model {Model}",
                filtered.Count,
                hits.Count,
                minSimilarity,
                sw.ElapsedMilliseconds,
                _embeddingService.ModelName);

            operation.Activity?.SetTag(AiTelemetryNames.Tags.LatencyMs, sw.ElapsedMilliseconds);
            operation.SetOutcome(AiTelemetryNames.Outcomes.Success);

            return new SemanticSearchPipelineResult
            {
                Hits = filtered,
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
