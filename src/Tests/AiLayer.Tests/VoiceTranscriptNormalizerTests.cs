using Fistix.TaskManager.AiLayer.Shared;

namespace Fistix.TaskManager.AiLayer.Tests;

public class VoiceTranscriptNormalizerTests
{
    [Fact]
    public void Normalize_FixesCommonWeekdaySlip()
    {
        var normalized = VoiceTranscriptNormalizer.Normalize("create a task on such a day");

        Assert.Equal("create a task on saturday", normalized);
    }

    [Fact]
    public void Normalize_FixesAddedToEdit_WhenEditContextOpen()
    {
        var normalized = VoiceTranscriptNormalizer.Normalize("added this title", "editOpen=true");

        Assert.Equal("edit this title", normalized);
    }

    [Fact]
    public void Normalize_DoesNotForceAddedWithoutEditContext()
    {
        var normalized = VoiceTranscriptNormalizer.Normalize("added this title");

        Assert.Equal("added this title", normalized);
    }

    [Theory]
    [InlineData("read the priority", "regenerate the priority")]
    [InlineData("read priority", "regenerate the priority")]
    [InlineData("regenerate the priority", "regenerate the priority")]
    public void Normalize_FixesRegeneratePrioritySlips(string input, string expected)
    {
        Assert.Equal(expected, VoiceTranscriptNormalizer.Normalize(input));
    }

    [Theory]
    [InlineData("regenerate the sunday of this task", "regenerate the summary of this task")]
    [InlineData("regenerate the sunday", "regenerate the summary")]
    [InlineData("the sunday of this task", "the summary of this task")]
    [InlineData("read the sunday", "regenerate the summary")]
    public void Normalize_FixesSummaryHeardAsSunday(string input, string expected)
    {
        Assert.Equal(expected, VoiceTranscriptNormalizer.Normalize(input));
    }

    [Theory]
    [InlineData("sunday last friday", "set due date last friday")]
    [InlineData("set due sunday last friday", "set due date last friday")]
    [InlineData("due sunday last friday", "set due date last friday")]
    public void Normalize_FixesDueDateHeardAsWeekdayPrefix(string input, string expected)
    {
        Assert.Equal(expected, VoiceTranscriptNormalizer.Normalize(input));
    }

    [Fact]
    public void Normalize_DoesNotRewriteSummaryToSundayWithoutCue()
    {
        // Regression: Levenshtein(summary, sunday)=3 used to rewrite summary → sunday.
        var normalized = VoiceTranscriptNormalizer.Normalize("please regenerate the summary");

        Assert.Equal("please regenerate the summary", normalized);
        Assert.DoesNotContain("sunday", normalized, StringComparison.OrdinalIgnoreCase);
    }
}
