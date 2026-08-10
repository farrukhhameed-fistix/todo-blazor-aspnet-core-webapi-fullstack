#nullable enable

using Fistix.TaskManager.AiLayer.Abstractions;
using Fistix.TaskManager.AiLayer.Implementations;

namespace Fistix.TaskManager.AiLayer.Tests;

public class HybridSearchFusionTests
{
    [Fact]
    public void FuseRrf_PrefersItemsInBothLanes()
    {
        var shared = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var vectorOnly = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var lexicalOnly = Guid.Parse("22222222-2222-2222-2222-222222222222");

        var vector = new List<VectorSearchHit>
        {
            new(shared, 1, 0.9),
            new(vectorOnly, 2, 0.8)
        };
        var lexical = new List<LexicalSearchHit>
        {
            new(shared, 1, 1.0),
            new(lexicalOnly, 3, 0.5)
        };

        var fused = HybridSearchFusion.FuseRrf(vector, lexical, rrfK: 60);
        var blended = HybridSearchFusion.BlendAndTake(fused, limit: 3);

        Assert.Equal(3, blended.Count);
        Assert.Equal(shared, blended[0].TodoExternalId);
    }

    [Fact]
    public void BlendAndTake_RespectsLimit()
    {
        var vector = Enumerable.Range(0, 5)
            .Select(i => new VectorSearchHit(Guid.NewGuid(), i + 1, 0.9 - (i * 0.05)))
            .ToList();

        var fused = HybridSearchFusion.FuseRrf(vector, [], rrfK: 60);
        var blended = HybridSearchFusion.BlendAndTake(fused, limit: 2);

        Assert.Equal(2, blended.Count);
    }

    [Fact]
    public void FuseRrf_EmptyInputs_ReturnsEmpty()
    {
        var fused = HybridSearchFusion.FuseRrf([], [], rrfK: 60);
        var blended = HybridSearchFusion.BlendAndTake(fused, limit: 10);
        Assert.Empty(blended);
    }
}
