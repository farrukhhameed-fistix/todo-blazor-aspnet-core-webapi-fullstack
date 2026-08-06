using System.Text.Json;
using Fistix.TaskManager.AiLayer.Shared;
using Fistix.TaskManager.AiLayer.Tools;

namespace Fistix.TaskManager.AiLayer.Tests;

public class ToolArgumentValidatorTests
{
    [Fact]
    public void CreateTodo_RequiresTitle()
    {
        var result = ToolArgumentValidator.Validate(
            TodoToolDefinitions.CreateTodo,
            new Dictionary<string, JsonElement>());

        Assert.False(result.IsValid);
        Assert.Contains("title", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UpdateTodo_RejectsInvalidStatus()
    {
        var args = new Dictionary<string, JsonElement>
        {
            ["id"] = JsonSerializer.SerializeToElement(Guid.NewGuid().ToString()),
            ["status"] = JsonSerializer.SerializeToElement("Exploded")
        };

        var result = ToolArgumentValidator.Validate(TodoToolDefinitions.UpdateTodo, args);

        Assert.False(result.IsValid);
        Assert.Contains("status", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UpdateTodo_AcceptsAllowlistedStatus()
    {
        var args = new Dictionary<string, JsonElement>
        {
            ["id"] = JsonSerializer.SerializeToElement(Guid.NewGuid().ToString()),
            ["status"] = JsonSerializer.SerializeToElement("InProgress")
        };

        var result = ToolArgumentValidator.Validate(TodoToolDefinitions.UpdateTodo, args);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void SearchTodos_RejectsOverlongQuery()
    {
        var args = new Dictionary<string, JsonElement>
        {
            ["query"] = JsonSerializer.SerializeToElement(new string('q', LlmInputLimits.ToolSearchQueryMaxLength + 1))
        };

        var result = ToolArgumentValidator.Validate(TodoToolDefinitions.SearchTodos, args);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void MarkComplete_RequiresGuid()
    {
        var args = new Dictionary<string, JsonElement>
        {
            ["id"] = JsonSerializer.SerializeToElement("not-a-guid")
        };

        var result = ToolArgumentValidator.Validate(TodoToolDefinitions.MarkComplete, args);

        Assert.False(result.IsValid);
    }
}
