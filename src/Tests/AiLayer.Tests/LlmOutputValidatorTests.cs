using Fistix.TaskManager.AiLayer.Shared;

namespace Fistix.TaskManager.AiLayer.Tests;

public class LlmOutputValidatorTests
{
    [Fact]
    public void ValidateSummary_ThrowsWhenEmpty()
    {
        Assert.Throws<InvalidOperationException>(() => LlmOutputValidator.ValidateSummary("   "));
    }

    [Fact]
    public void ValidateSummary_TruncatesWhenOverMaxLength()
    {
        var longSummary = new string('x', LlmInputLimits.SummaryMaxLength + 50);

        var result = LlmOutputValidator.ValidateSummary(longSummary);

        Assert.Equal(LlmInputLimits.SummaryMaxLength, result.Length);
    }

    [Fact]
    public void ValidateSummary_ReturnsTrimmedSummary()
    {
        var result = LlmOutputValidator.ValidateSummary("  Hello world  ");

        Assert.Equal("Hello world", result);
    }

    [Fact]
    public void ValidateSummary_StripsControlCharacters()
    {
        var result = LlmOutputValidator.ValidateSummary("Hello\u0000world");

        Assert.Equal("Helloworld", result);
    }

    [Fact]
    public void ValidateClassificationJson_RequiresPriorityAndConfidence()
    {
        Assert.Throws<InvalidOperationException>(() =>
            LlmOutputValidator.ValidateClassificationJson("""{"reason":"x"}"""));
    }

    [Fact]
    public void ValidateClassificationJson_ParsesValidPayload()
    {
        var (priority, confidence, reason) = LlmOutputValidator.ValidateClassificationJson(
            """{"priority":"high","confidence":0.91,"reason":"blocker"}""");

        Assert.Equal("HIGH", priority);
        Assert.Equal(0.91f, confidence);
        Assert.Equal("blocker", reason);
    }

    [Fact]
    public void ValidateClassificationJson_TruncatesReason()
    {
        var longReason = new string('r', LlmInputLimits.ReasonMaxLength + 40);
        var (_, _, reason) = LlmOutputValidator.ValidateClassificationJson(
            $"{{\"priority\":\"LOW\",\"confidence\":0.2,\"reason\":\"{longReason}\"}}");

        Assert.Equal(LlmInputLimits.ReasonMaxLength, reason!.Length);
    }

    [Fact]
    public void ValidateRagAnswer_RejectsForeignGuid()
    {
        var allowed = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var foreign = Guid.Parse("11111111-1111-1111-1111-111111111111");

        Assert.Throws<InvalidOperationException>(() =>
            LlmOutputValidator.ValidateRagAnswer(
                $"See task {foreign}",
                [allowed]));
    }

    [Fact]
    public void ValidateRagAnswer_AllowsCitedSourceGuid()
    {
        var id = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var answer = LlmOutputValidator.ValidateRagAnswer($"Task {id} is overdue.", [id]);

        Assert.Contains(id.ToString(), answer, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateAgentText_SanitizesAndTruncates()
    {
        var text = LlmOutputValidator.ValidateAgentText("Hello {world}", maxLength: 20);

        Assert.Contains("{{", text);
        Assert.True(text.Length <= 20);
    }
}
