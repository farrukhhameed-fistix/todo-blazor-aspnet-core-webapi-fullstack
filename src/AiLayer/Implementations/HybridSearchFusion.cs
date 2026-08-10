#nullable enable

using Fistix.TaskManager.AiLayer.Abstractions;

namespace Fistix.TaskManager.AiLayer.Implementations;

/// <summary>Reciprocal Rank Fusion and light score blending for hybrid retrieval.</summary>
public static class HybridSearchFusion
{
    /// <summary>
    /// RRF: score(d) = Σ 1/(k + rank_i(d)) with 1-based ranks.
    /// </summary>
    public static IReadOnlyList<FusedCandidate> FuseRrf(
        IReadOnlyList<VectorSearchHit> vectorHits,
        IReadOnlyList<LexicalSearchHit> lexicalHits,
        int rrfK)
    {
        var k = Math.Max(1, rrfK);
        var byId = new Dictionary<Guid, FusedCandidate>();

        for (var i = 0; i < vectorHits.Count; i++)
        {
            var hit = vectorHits[i];
            var rank = i + 1;
            if (!byId.TryGetValue(hit.TodoExternalId, out var c))
            {
                c = new FusedCandidate(hit.TodoExternalId, hit.TodoId);
                byId[hit.TodoExternalId] = c;
            }

            c.RrfScore += 1.0 / (k + rank);
            c.VectorSimilarity = hit.Similarity;
            c.TodoId = hit.TodoId;
        }

        for (var i = 0; i < lexicalHits.Count; i++)
        {
            var hit = lexicalHits[i];
            var rank = i + 1;
            if (!byId.TryGetValue(hit.TodoExternalId, out var c))
            {
                c = new FusedCandidate(hit.TodoExternalId, hit.TodoId);
                byId[hit.TodoExternalId] = c;
            }

            c.RrfScore += 1.0 / (k + rank);
            c.LexicalRank = hit.Rank;
            c.TodoId = hit.TodoId;
        }

        return byId.Values.ToList();
    }

    /// <summary>
    /// Sort by RRF plus a light boost from vector similarity and normalized FTS rank.
    /// Display Similarity is clamped to 0–1 for UI compatibility.
    /// </summary>
    public static IReadOnlyList<VectorSearchHit> BlendAndTake(
        IReadOnlyList<FusedCandidate> fused,
        int limit)
    {
        if (fused.Count == 0 || limit <= 0)
        {
            return Array.Empty<VectorSearchHit>();
        }

        var maxLexical = fused.Where(c => c.LexicalRank.HasValue).Select(c => c.LexicalRank!.Value).DefaultIfEmpty(0).Max();
        var ranked = fused
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
            .Select(x => new VectorSearchHit(x.c.TodoExternalId, x.c.TodoId, x.display))
            .ToList();

        return ranked;
    }
}

public sealed class FusedCandidate
{
    public FusedCandidate(Guid todoExternalId, int todoId)
    {
        TodoExternalId = todoExternalId;
        TodoId = todoId;
    }

    public Guid TodoExternalId { get; }
    public int TodoId { get; set; }
    public double RrfScore { get; set; }
    public double? VectorSimilarity { get; set; }
    public double? LexicalRank { get; set; }
}
