using Fistix.TaskManager.Core.DomainModel.Aggregates;
using Fistix.TaskManager.Core.DomainModel.Constants;
using Fistix.TaskManager.ServiceLayer.Todos;
using Xunit;

namespace Fistix.TaskManager.ServiceLayer.Tests;

public class AiBatchJobMapperTests
{
    [Fact]
    public void RoundTrip_SerializeIds()
    {
        var ids = new List<Guid> { Guid.NewGuid(), Guid.NewGuid() };
        var json = AiBatchJobMapper.SerializeIds(ids);
        var back = AiBatchJobMapper.DeserializeIds(json);
        Assert.Equal(ids, back);
    }

    [Fact]
    public void DeserializeIds_EmptyOrWhitespace_ReturnsEmpty()
    {
        Assert.Empty(AiBatchJobMapper.DeserializeIds(""));
        Assert.Empty(AiBatchJobMapper.DeserializeIds("   "));
    }

    [Fact]
    public void ToDto_ComputesPercentAcrossSteps()
    {
        var job = new AiBatchJob
        {
            StepsCsv = "Embedding,Classify",
            CurrentStep = "Classify",
            Cursor = 5,
            Total = 10,
            Status = AiBatchJobStatus.Running,
            TodoExternalIdsJson = "[]"
        };
        job.GenerateNewExternalId();

        var dto = AiBatchJobMapper.ToDto(job);

        // step 1 complete (10) + 5 of step 2 => 15/20 = 75%
        Assert.Equal(75.0, dto.PercentComplete);
        Assert.Equal(2, dto.Steps.Count);
    }

    [Fact]
    public void ToDto_ZeroTotal_ReturnsZeroPercent()
    {
        var job = new AiBatchJob
        {
            StepsCsv = "Embedding",
            CurrentStep = "Embedding",
            Cursor = 0,
            Total = 0,
            Status = AiBatchJobStatus.Pending,
            TodoExternalIdsJson = "[]"
        };
        job.GenerateNewExternalId();

        var dto = AiBatchJobMapper.ToDto(job);

        Assert.Equal(0, dto.PercentComplete);
    }

    [Fact]
    public void ParseSteps_NormalizesCaseAndDedupes()
    {
        var steps = AiBatchJobMapper.ParseSteps("embedding, Classify, EMBEDDING, summarize");

        Assert.Equal(
            new[] { AiBatchStepNames.Embedding, AiBatchStepNames.Classify, AiBatchStepNames.Summarize },
            steps);
    }

    [Fact]
    public void ParseSteps_Blank_ReturnsDefaults()
    {
        var steps = AiBatchJobMapper.ParseSteps("  ");

        Assert.Equal(AiBatchStepNames.DefaultSteps, steps);
    }

    [Fact]
    public void ParseSteps_Unknown_Throws()
    {
        Assert.Throws<ArgumentException>(() => AiBatchJobMapper.ParseSteps("Translate"));
    }

    [Fact]
    public void StepsToCsv_Normalizes()
    {
        var csv = AiBatchJobMapper.StepsToCsv(["classify", "Embedding"]);

        Assert.Equal("Classify,Embedding", csv);
    }
}
