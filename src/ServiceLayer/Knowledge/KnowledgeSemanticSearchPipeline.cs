#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fistix.TaskManager.AiLayer.Abstractions;
using Fistix.TaskManager.AiLayer.Implementations;
using Fistix.TaskManager.AiLayer.Shared;
using Fistix.TaskManager.Core.Abstractions.Repositories;
using Microsoft.Extensions.Logging;

namespace Fistix.TaskManager.ServiceLayer.Knowledge;

public sealed class KnowledgeSemanticSearchRequest
{
    public string Query { get; set; } = string.Empty;
    public Guid OwnerExternalId { get; set; }
    public Guid? DocumentExternalId { get; set; }
    public int Limit { get; set; } = 5;
    public IReadOnlyCollection<Guid>? ExcludeChunkExternalIds { get; set; }
}

public sealed class KnowledgeRetrievedChunk
{
    public Guid ChunkExternalId { get; set; }
    public int ChunkId { get; set; }
    public Guid DocumentExternalId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public int Ordinal { get; set; }
    public string Content { get; set; } = string.Empty;
    public string? Heading { get; set; }
    public double Similarity { get; set; }
    public bool FromVector { get; set; }
    public bool FromLexical { get; set; }
}

public sealed class KnowledgeSemanticSearchResult
{
    public IReadOnlyList<KnowledgeRetrievedChunk> Hits { get; set; } = Array.Empty<KnowledgeRetrievedChunk>();
    public bool HybridUsed { get; set; }
    public int VectorCandidateCount { get; set; }
    public int LexicalCandidateCount { get; set; }
}

/// <summary>Vector-only or hybrid (FTS + RRF) retrieval for knowledge chunks.</summary>
public sealed class KnowledgeSemanticSearchPipeline
{
    private readonly IEmbeddingService _embeddingService;
    private readonly IKnowledgeChunkEmbeddingRepository _embeddings;
    private readonly IKnowledgeLexicalSearchRepository _lexical;
    private readonly AiConfiguration _aiConfig;
    private readonly ILogger<KnowledgeSemanticSearchPipeline> _logger;

    public KnowledgeSemanticSearchPipeline(
        IEmbeddingService embeddingService,
        IKnowledgeChunkEmbeddingRepository embeddings,
        IKnowledgeLexicalSearchRepository lexical,
        AiConfiguration aiConfig,
        ILogger<KnowledgeSemanticSearchPipeline> logger)
    {
        _embeddingService = embeddingService;
        _embeddings = embeddings;
        _lexical = lexical;
        _aiConfig = aiConfig;
        _logger = logger;
    }

    public async Task<KnowledgeSemanticSearchResult> ExecuteAsync(
        KnowledgeSemanticSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        var cfg = _aiConfig.Features.KnowledgeRag ?? new KnowledgeRagConfiguration();
        var limit = Math.Clamp(request.Limit <= 0 ? 5 : request.Limit, 1, 25);
        if (string.IsNullOrWhiteSpace(request.Query))
        {
            return new KnowledgeSemanticSearchResult();
        }

        if (cfg.HybridEnabled)
        {
            return await ExecuteHybridAsync(request, cfg, limit, cancellationToken);
        }

        return await ExecuteVectorOnlyAsync(request, cfg, limit, cancellationToken);
    }

    private async Task<KnowledgeSemanticSearchResult> ExecuteVectorOnlyAsync(
        KnowledgeSemanticSearchRequest request,
        KnowledgeRagConfiguration cfg,
        int limit,
        CancellationToken cancellationToken)
    {
        var embedding = await _embeddingService.GenerateEmbeddingAsync(
            request.Query, EmbeddingInputKind.Query, cancellationToken);
        var raw = await _embeddings.SearchSimilarAsync(
            embedding,
            _embeddingService.ModelName,
            request.OwnerExternalId,
            Math.Max(limit * 2, limit),
            cancellationToken,
            request.DocumentExternalId,
            request.ExcludeChunkExternalIds);

        var kept = raw
            .Select(ToRetrieved)
            .Where(h => h.Similarity >= cfg.MinSimilarity)
            .Take(limit)
            .ToList();

        return new KnowledgeSemanticSearchResult
        {
            Hits = kept,
            HybridUsed = false,
            VectorCandidateCount = raw.Count
        };
    }

    private async Task<KnowledgeSemanticSearchResult> ExecuteHybridAsync(
        KnowledgeSemanticSearchRequest request,
        KnowledgeRagConfiguration cfg,
        int limit,
        CancellationToken cancellationToken)
    {
        var vectorLimit = Math.Clamp(cfg.VectorCandidateLimit, limit, 200);
        var lexicalLimit = Math.Clamp(cfg.LexicalCandidateLimit, limit, 200);

        var embedding = await _embeddingService.GenerateEmbeddingAsync(
            request.Query, EmbeddingInputKind.Query, cancellationToken);

        // Sequential: same scoped DbContext.
        var vectorRaw = await _embeddings.SearchSimilarAsync(
            embedding,
            _embeddingService.ModelName,
            request.OwnerExternalId,
            vectorLimit,
            cancellationToken,
            request.DocumentExternalId,
            request.ExcludeChunkExternalIds);

        var lexicalRaw = await _lexical.SearchAsync(
            request.Query,
            request.OwnerExternalId,
            lexicalLimit,
            cancellationToken,
            request.DocumentExternalId,
            request.ExcludeChunkExternalIds);

        var vectorHits = vectorRaw
            .Select(h => new RankedHit(h.ChunkExternalId, h.ChunkId, Math.Max(0, 1.0 - h.Distance)))
            .ToList();

        var strongVector = vectorHits.Where(h => h.Score >= cfg.MinSimilarity).ToList();
        if (strongVector.Count == 0 && vectorHits.Count > 0)
        {
            strongVector = vectorHits.Take(Math.Min(5, vectorHits.Count)).ToList();
        }

        var lexicalHits = lexicalRaw
            .Select(h => new RankedHit(h.ChunkExternalId, h.ChunkId, h.Rank))
            .ToList();

        var fused = HybridSearchFusion.FuseRrf(strongVector, lexicalHits, cfg.RrfK);
        var blended = HybridSearchFusion.BlendAndTake(fused, limit);

        var meta = new Dictionary<Guid, KnowledgeRetrievedChunk>();
        foreach (var h in vectorRaw)
        {
            var sim = Math.Max(0, 1.0 - h.Distance);
            meta[h.ChunkExternalId] = new KnowledgeRetrievedChunk
            {
                ChunkExternalId = h.ChunkExternalId,
                ChunkId = h.ChunkId,
                DocumentExternalId = h.DocumentExternalId,
                FileName = h.FileName,
                Ordinal = h.Ordinal,
                Content = h.Content,
                Heading = h.Heading,
                Similarity = sim,
                FromVector = true
            };
        }

        foreach (var h in lexicalRaw)
        {
            if (meta.TryGetValue(h.ChunkExternalId, out var existing))
            {
                existing.FromLexical = true;
                continue;
            }

            meta[h.ChunkExternalId] = new KnowledgeRetrievedChunk
            {
                ChunkExternalId = h.ChunkExternalId,
                ChunkId = h.ChunkId,
                DocumentExternalId = h.DocumentExternalId,
                FileName = h.FileName,
                Ordinal = h.Ordinal,
                Content = h.Content,
                Heading = h.Heading,
                Similarity = 0,
                FromLexical = true
            };
        }

        var results = new List<KnowledgeRetrievedChunk>();
        foreach (var hit in blended)
        {
            if (!meta.TryGetValue(hit.ExternalId, out var chunk))
            {
                continue;
            }

            chunk.Similarity = hit.Score;
            var fusedCand = fused.FirstOrDefault(f => f.ExternalId == hit.ExternalId);
            chunk.FromVector = fusedCand?.VectorSimilarity.HasValue == true;
            chunk.FromLexical = fusedCand?.LexicalRank.HasValue == true;
            results.Add(chunk);
        }

        _logger.LogInformation(
            "Knowledge hybrid fuse vector={VectorCount} lexical={LexicalCount} out={OutCount}",
            strongVector.Count,
            lexicalHits.Count,
            results.Count);

        return new KnowledgeSemanticSearchResult
        {
            Hits = results,
            HybridUsed = true,
            VectorCandidateCount = vectorRaw.Count,
            LexicalCandidateCount = lexicalRaw.Count
        };
    }

    private static KnowledgeRetrievedChunk ToRetrieved(KnowledgeChunkSearchHit h) =>
        new()
        {
            ChunkExternalId = h.ChunkExternalId,
            ChunkId = h.ChunkId,
            DocumentExternalId = h.DocumentExternalId,
            FileName = h.FileName,
            Ordinal = h.Ordinal,
            Content = h.Content,
            Heading = h.Heading,
            Similarity = Math.Max(0, 1.0 - h.Distance),
            FromVector = true
        };
}
