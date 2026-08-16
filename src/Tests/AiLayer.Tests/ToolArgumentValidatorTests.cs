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
    public void SearchTodos_AcceptsOptionalSemanticFlag()
    {
        var args = new Dictionary<string, JsonElement>
        {
            ["query"] = JsonSerializer.SerializeToElement("payments"),
            ["semantic"] = JsonSerializer.SerializeToElement(true)
        };

        var result = ToolArgumentValidator.Validate(TodoToolDefinitions.SearchTodos, args);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void SearchTodos_AcceptsStatusAndDueFiltersWithoutQuery()
    {
        var args = new Dictionary<string, JsonElement>
        {
            ["status"] = JsonSerializer.SerializeToElement("Pending"),
            ["dueFrom"] = JsonSerializer.SerializeToElement("2026-08-04"),
            ["dueTo"] = JsonSerializer.SerializeToElement("2026-08-10")
        };

        var result = ToolArgumentValidator.Validate(TodoToolDefinitions.SearchTodos, args);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void SearchTodos_AcceptsEmptySearch_AsShowAll()
    {
        var result = ToolArgumentValidator.Validate(
            TodoToolDefinitions.SearchTodos,
            new Dictionary<string, JsonElement>());

        Assert.True(result.IsValid);
    }

    [Fact]
    public void SetSemanticSearch_RequiresEnabledBoolean()
    {
        var missing = ToolArgumentValidator.Validate(
            TodoToolDefinitions.SetSemanticSearch,
            new Dictionary<string, JsonElement>());

        Assert.False(missing.IsValid);

        var ok = ToolArgumentValidator.Validate(
            TodoToolDefinitions.SetSemanticSearch,
            new Dictionary<string, JsonElement>
            {
                ["enabled"] = JsonSerializer.SerializeToElement(false)
            });

        Assert.True(ok.IsValid);
    }

    [Fact]
    public void MarkComplete_RequiresGuidOrIndex()
    {
        var badId = ToolArgumentValidator.Validate(
            TodoToolDefinitions.MarkComplete,
            new Dictionary<string, JsonElement>
            {
                ["id"] = JsonSerializer.SerializeToElement("not-a-guid")
            });
        Assert.False(badId.IsValid);

        var byIndex = ToolArgumentValidator.Validate(
            TodoToolDefinitions.MarkComplete,
            new Dictionary<string, JsonElement>
            {
                ["index"] = JsonSerializer.SerializeToElement(3)
            });
        Assert.True(byIndex.IsValid);
    }

    [Fact]
    public void OpenTodo_RequiresPositiveIndex()
    {
        var missing = ToolArgumentValidator.Validate(
            TodoToolDefinitions.OpenTodo,
            new Dictionary<string, JsonElement>());
        Assert.False(missing.IsValid);

        var ok = ToolArgumentValidator.Validate(
            TodoToolDefinitions.OpenTodo,
            new Dictionary<string, JsonElement>
            {
                ["index"] = JsonSerializer.SerializeToElement(1)
            });
        Assert.True(ok.IsValid);
    }

    [Fact]
    public void UpdateTodo_AcceptsIndexWithoutId()
    {
        var result = ToolArgumentValidator.Validate(
            TodoToolDefinitions.UpdateTodo,
            new Dictionary<string, JsonElement>
            {
                ["index"] = JsonSerializer.SerializeToElement(2),
                ["priority"] = JsonSerializer.SerializeToElement("High")
            });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void UpdateTodo_AllowsOmittedIdAndIndex_WhenAFieldIsPresent()
    {
        var result = ToolArgumentValidator.Validate(
            TodoToolDefinitions.UpdateTodo,
            new Dictionary<string, JsonElement>
            {
                ["priority"] = JsonSerializer.SerializeToElement("Medium")
            });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void UpdateTodo_RejectsWhenNoTargetAndNoFields()
    {
        var result = ToolArgumentValidator.Validate(
            TodoToolDefinitions.UpdateTodo,
            new Dictionary<string, JsonElement>());

        Assert.False(result.IsValid);
        Assert.Contains("at least one field", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SetPriority_AllowsOmittedIdAndIndex()
    {
        var result = ToolArgumentValidator.Validate(
            TodoToolDefinitions.SetPriority,
            new Dictionary<string, JsonElement>
            {
                ["priority"] = JsonSerializer.SerializeToElement("Low")
            });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void SetPriority_StillRequiresPriority()
    {
        var result = ToolArgumentValidator.Validate(
            TodoToolDefinitions.SetPriority,
            new Dictionary<string, JsonElement>());

        Assert.False(result.IsValid);
        Assert.Contains("priority", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UpdateTodo_RejectsInvalidIdWhenProvided()
    {
        var result = ToolArgumentValidator.Validate(
            TodoToolDefinitions.UpdateTodo,
            new Dictionary<string, JsonElement>
            {
                ["id"] = JsonSerializer.SerializeToElement("not-a-guid"),
                ["title"] = JsonSerializer.SerializeToElement("Renamed")
            });

        Assert.False(result.IsValid);
    }

    [Fact]
    public void RegenerateSummary_AllowsMissingIndex()
    {
        var result = ToolArgumentValidator.Validate(
            TodoToolDefinitions.RegenerateSummary,
            new Dictionary<string, JsonElement>());
        Assert.True(result.IsValid);
    }
}
