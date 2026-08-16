using System.Text.Json;
using Fistix.TaskManager.AiLayer.Models;
using Fistix.TaskManager.AiLayer.Shared;
using Fistix.TaskManager.AiLayer.Tools;

namespace Fistix.TaskManager.AiLayer.Tests;

public class RelativeDueDateResolverTests
{
    // Tuesday, 11 Aug 2026
    private static readonly DateTime Today = new(2026, 8, 11, 0, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("Create a task to visit the dentist on coming Sunday", "2026-08-16")]
    [InlineData("remind me next Sunday", "2026-08-16")]
    [InlineData("due this Sunday", "2026-08-16")]
    [InlineData("schedule for tomorrow", "2026-08-12")]
    [InlineData("Create a task due on Friday", "2026-08-14")]
    [InlineData("set due date last friday", "2026-08-07")]
    public void TryResolveFromPrompt_ResolvesRelativeWeekdays(string prompt, string expectedIso)
    {
        Assert.True(RelativeDueDateResolver.TryResolveFromPrompt(prompt, Today, out var due));
        Assert.Equal(expectedIso, due.ToString("yyyy-MM-dd"));
    }

    [Fact]
    public void NextOrSameWeekday_ComingSundayFromTuesday_IsAugust16()
    {
        var due = RelativeDueDateResolver.NextOrSameWeekday(Today, DayOfWeek.Sunday, allowToday: false);
        Assert.Equal(new DateTime(2026, 8, 16), due);
    }

    [Fact]
    public void ApplyToProposedCalls_OverwritesWrongLlmDueDate()
    {
        var calls = new List<ProposedToolCall>
        {
            new()
            {
                ToolName = TodoToolDefinitions.CreateTodo,
                Arguments = new Dictionary<string, JsonElement>
                {
                    ["title"] = JsonSerializer.SerializeToElement("Dentist"),
                    ["dueDate"] = JsonSerializer.SerializeToElement("2026-08-19")
                }
            }
        };

        RelativeDueDateResolver.ApplyToProposedCalls(
            "Create a task to visit the dentist on coming Sunday",
            calls,
            Today);

        Assert.Equal("2026-08-16", calls[0].Arguments!["dueDate"].GetString());
    }

    [Fact]
    public void ApplyToProposedCalls_IgnoresNonCreateUpdateTools()
    {
        var calls = new List<ProposedToolCall>
        {
            new()
            {
                ToolName = TodoToolDefinitions.SearchTodos,
                Arguments = new Dictionary<string, JsonElement>()
            }
        };

        RelativeDueDateResolver.ApplyToProposedCalls("coming Sunday", calls, Today);

        Assert.False(calls[0].Arguments!.ContainsKey("dueDate"));
    }
}
