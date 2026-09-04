#nullable enable

using System;
using Fistix.TaskManager.ServiceLayer.Todos;
using Xunit;

namespace Fistix.TaskManager.ServiceLayer.Tests;

public class AnalystOutputParserTests
{
    [Fact]
    public void ParseJson_ExtractsRecommendedIds()
    {
        var id = Guid.NewGuid();
        var text = $$"""
            {
              "recommendedIds": ["{{id}}"],
              "risks": ["overdue workload"],
              "theme": "delivery",
              "summary": "Focus on due-soon items."
            }
            """;

        var output = AnalystOutputParser.Parse(text, [id]);

        Assert.Single(output.RecommendedIds);
        Assert.Equal(id, output.RecommendedIds[0]);
        Assert.Equal("delivery", output.Theme);
        Assert.Contains("overdue workload", output.Risks);
    }

    [Fact]
    public void ParseProse_ExtractsGuidsAndFiltersUnknown()
    {
        var valid = Guid.NewGuid();
        var text = $"Recommend {valid} and {Guid.NewGuid()} for the sprint.";

        var output = AnalystOutputParser.Parse(text, [valid]);

        Assert.Single(output.RecommendedIds);
        Assert.Equal(valid, output.RecommendedIds[0]);
    }
}
