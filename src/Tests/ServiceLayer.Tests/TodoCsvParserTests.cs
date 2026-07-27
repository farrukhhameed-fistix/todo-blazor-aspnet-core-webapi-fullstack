using Fistix.TaskManager.ServiceLayer.Todos;
using Xunit;

namespace Fistix.TaskManager.ServiceLayer.Tests;

public class TodoCsvParserTests
{
    [Fact]
    public void Parse_ValidRows_ReturnsTodos()
    {
        var csv = """
            Title,Description,DueDate,Status,Priority,Category,ExpectedPriority
            Task A,"Desc A",2026-08-01,Pending,High,Auth,HIGH
            Task B,"Desc B",2026-08-02,,,
            """;

        var result = TodoCsvParser.Parse(csv);

        Assert.Empty(result.Errors);
        Assert.Equal(2, result.Rows.Count);
        Assert.Equal("Task A", result.Rows[0].Title);
        Assert.Equal("High", result.Rows[0].Priority);
        Assert.Equal("Pending", result.Rows[1].Status);
        Assert.Equal("Medium", result.Rows[1].Priority);
    }

    [Fact]
    public void Parse_QuotedComma_PreservesField()
    {
        var csv = """
            Title,Description,DueDate
            T1,"Hello, world",2026-08-01
            """;

        var result = TodoCsvParser.Parse(csv);

        Assert.Empty(result.Errors);
        Assert.Equal("Hello, world", result.Rows[0].Description);
    }

    [Fact]
    public void Parse_EscapedQuotes_PreservesField()
    {
        var csv = "Title,Description,DueDate\nT1,\"Say \"\"hello\"\"\",2026-08-01\n";

        var result = TodoCsvParser.Parse(csv);

        Assert.Empty(result.Errors);
        Assert.Equal("Say \"hello\"", result.Rows[0].Description);
    }

    [Fact]
    public void Parse_InvalidPriority_AddsError()
    {
        var csv = """
            Title,Description,DueDate,Priority
            T1,D1,2026-08-01,Urgent
            """;

        var result = TodoCsvParser.Parse(csv);

        Assert.Empty(result.Rows);
        Assert.Single(result.Errors);
        Assert.Contains("Priority", result.Errors[0].Message);
    }

    [Fact]
    public void Parse_InvalidStatus_AddsError()
    {
        var csv = """
            Title,Description,DueDate,Status
            T1,D1,2026-08-01,Blocked
            """;

        var result = TodoCsvParser.Parse(csv);

        Assert.Empty(result.Rows);
        Assert.Contains(result.Errors, e => e.Message.Contains("Status", StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_AllowsPastDueDate()
    {
        var csv = """
            Title,Description,DueDate
            Overdue,Was due last week,2020-01-01
            """;

        var result = TodoCsvParser.Parse(csv);

        Assert.Empty(result.Errors);
        Assert.Single(result.Rows);
        Assert.Equal(2020, result.Rows[0].DueDate.Year);
    }

    [Fact]
    public void Parse_EmptyContent_ReturnsError()
    {
        var result = TodoCsvParser.Parse("   ");

        Assert.Empty(result.Rows);
        Assert.Contains(result.Errors, e => e.Message.Contains("empty", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Parse_MissingRequiredColumns_ReturnsError()
    {
        var csv = """
            Title,Priority
            T1,High
            """;

        var result = TodoCsvParser.Parse(csv);

        Assert.Empty(result.Rows);
        Assert.Contains(result.Errors, e => e.Message.Contains("Title, Description, and DueDate", StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_BlankLines_AreIgnored()
    {
        var csv = """
            Title,Description,DueDate
            T1,D1,2026-08-01

            T2,D2,2026-08-02
            """;

        var result = TodoCsvParser.Parse(csv);

        Assert.Empty(result.Errors);
        Assert.Equal(2, result.Rows.Count);
    }

    [Fact]
    public void Parse_TitleTooLong_AddsError()
    {
        var title = new string('t', 201);
        var csv = $"""
            Title,Description,DueDate
            {title},D1,2026-08-01
            """;

        var result = TodoCsvParser.Parse(csv);

        Assert.Empty(result.Rows);
        Assert.Contains(result.Errors, e => e.Message.Contains("200", StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_ExceedsMaxRows_AddsErrorAndStops()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Title,Description,DueDate");
        for (var i = 0; i < 3; i++)
        {
            sb.AppendLine($"T{i},D{i},2026-08-01");
        }

        var result = TodoCsvParser.Parse(sb.ToString(), maxRows: 2);

        Assert.Equal(2, result.Rows.Count);
        Assert.Contains(result.Errors, e => e.Message.Contains("Maximum of 2", StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_MixedValidAndInvalid_KeepsValidRows()
    {
        var csv = """
            Title,Description,DueDate,Priority
            Good,Ok,2026-08-01,High
            Bad,Ok,2026-08-01,Urgent
            AlsoGood,Ok,2026-08-02,Low
            """;

        var result = TodoCsvParser.Parse(csv);

        Assert.Equal(2, result.Rows.Count);
        Assert.Single(result.Errors);
        Assert.Equal("Good", result.Rows[0].Title);
        Assert.Equal("AlsoGood", result.Rows[1].Title);
    }

    [Fact]
    public void Parse_MissingTitle_AddsError()
    {
        var csv = """
            Title,Description,DueDate
            ,Desc,2026-08-01
            """;

        var result = TodoCsvParser.Parse(csv);

        Assert.Empty(result.Rows);
        Assert.Contains(result.Errors, e => e.Message.Contains("Title is required", StringComparison.Ordinal));
    }
}
