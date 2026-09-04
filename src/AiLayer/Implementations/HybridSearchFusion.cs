#nullable enable

using Fistix.TaskManager.AiLayer.Abstractions;

namespace Fistix.TaskManager.AiLayer.Implementations;

/// <summary>Neutral ranked hit used by RRF fusion (todos, knowledge chunks, etc.).</summary>
public sealed record RankedHit(Guid ExternalId, int InternalId, double Score);

/// <summary>Reciprocal Rank Fusion and light score blending for hybrid retrieval.</summary>
public static class HybridSearchFusion
{
    /// <summary>
    /// RRF: score(d) = Σ 1/(k + rank_i(d)) with 1-based ranks.
    /// </summary>
    public static IReadOnlyList<FusedCandidate> FuseRrf(
        IReadOnlyList<RankedHit> vectorHits,
        IReadOnlyList<RankedHit> lexicalHits,
        int rrfK)
    {
        var k = Math.Max(1, rrfK);
        var byId = new Dictionary<Guid, FusedCandidate>();

        for (var i = 0; i < vectorHits.Count; i++)
        {
            var hit = vectorHits[i];
            var rank = i + 1;
            if (!byId.TryGetValue(hit.ExternalId, out var c))
            {
                c = new FusedCandidate(hit.ExternalId, hit.InternalId);
                byId[hit.ExternalId] = c;
            }

            c.RrfScore += 1.0 / (k + rank);
            c.VectorSimilarity = hit.Score;
            c.InternalId = hit.InternalId;
        }

        for (var i = 0; i < lexicalHits.Count; i++)
        {
            var hit = lexicalHits[i];
            var rank = i + 1;
            if (!byId.TryGetValue(hit.ExternalId, out var c))
            {
                c = new FusedCandidate(hit.ExternalId, hit.InternalId);
                byId[hit.ExternalId] = c;
            }

            c.RrfScore += 1.0 / (k + rank);
            c.LexicalRank = hit.Score;
            c.InternalId = hit.InternalId;
        }

        return byId.Values.ToList();
    }

    /// <summary>Todo-shaped convenience overload.</summary>
    public static IReadOnlyList<FusedCandidate> FuseRrf(
        IReadOnlyList<VectorSearchHit> vectorHits,
        IReadOnlyList<LexicalSearchHit> lexicalHits,
        int rrfK) =>
        FuseRrf(
            vectorHits.Select(h => new RankedHit(h.TodoExternalId, h.TodoId, h.Similarity)).ToList(),
            lexicalHits.Select(h => new RankedHit(h.TodoExternalId, h.TodoId, h.Rank)).ToList(),
            rrfK);

    /// <summary>
    /// Sort by RRF plus a light boost from vector similarity and normalized FTS rank.
    /// Display Similarity is clamped to 0–1 for UI compatibility.
    /// </summary>
    public static IReadOnlyList<RankedHit> BlendAndTake(
        IReadOnlyList<FusedCandidate> fused,
        int limit)
    {
        if (fused.Count == 0 || limit <= 0)
        {
            return Array.Empty<RankedHit>();
        }

        var maxLexical = fused.Where(c => c.LexicalRank.HasValue).Select(c => c.LexicalRank!.Value).DefaultIfEmpty(0).Max();
        return fused
            .Select(c =>
            {
                var lexicalNorm = maxLexical > 0 && c.LexicalRank.HasValue
                    ? c.LexicalRank.Value / maxLexical
                    : 0.0;
                var vectorPart = c.VectorSimilarity ?? 0.0;
                var blend = c.RrfScore + (0.15 * vectorPart) + (0.15 * lexicalNorm);
                var display = c.VectorSimilarity
                              ?? (c.LexicalRank.HasValue ? Math.Clamp(0.45 + (0.5 * lexicalNorm), 0, 1) : Math.Clamp(c.RrfScore, 0, 1));
                return (c, blend, display);
            })
            .OrderByDescending(x => x.blend)
            .ThenByDescending(x => x.display)
            .Take(limit)
            .Select(x => new RankedHit(x.c.ExternalId, x.c.InternalId, x.display))
            .ToList();
    }

    /// <summary>Todo-shaped convenience: returns <see cref="VectorSearchHit"/>.</summary>
    public static IReadOnlyList<VectorSearchHit> BlendAndTakeAsVectorHits(
        IReadOnlyList<FusedCandidate> fused,
        int limit) =>
        BlendAndTake(fused, limit)
            .Select(h => new VectorSearchHit(h.ExternalId, h.InternalId, h.Score))
            .ToList();
}

public sealed class FusedCandidate
{
    public FusedCandidate(Guid externalId, int internalId)
    {
        ExternalId = externalId;
        InternalId = internalId;
    }

    public Guid ExternalId { get; }
    public int InternalId { get; set; }
    public double RrfScore { get; set; }
    public double? VectorSimilarity { get; set; }
    public double? LexicalRank { get; set; }

    /// <summary>Backward-compatible alias for todo pipelines.</summary>
    public Guid TodoExternalId => ExternalId;

    /// <summary>Backward-compatible alias for todo pipelines.</summary>
    public int TodoId
    {
        get => InternalId;
        set => InternalId = value;
    }
}
