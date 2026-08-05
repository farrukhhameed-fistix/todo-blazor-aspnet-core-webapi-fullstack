#nullable enable

using Fistix.TaskManager.AiLayer.Observability;
using Fistix.TaskManager.AiLayer.Shared;
using Xunit;

namespace Fistix.TaskManager.AiLayer.Tests;

public class AiPayloadRedactorTests
{
    [Fact]
    public void Preview_WhenDisabled_ReturnsNull()
    {
        var settings = new AiObservabilitySettings
        {
            CapturePayloadPreview = false,
            PayloadPreviewMaxChars = 256
        };

        Assert.Null(AiPayloadRedactor.Preview("secret prompt text", settings));
    }

    [Fact]
    public void Preview_WhenEnabled_Truncates()
    {
        var settings = new AiObservabilitySettings
        {
            CapturePayloadPreview = true,
            PayloadPreviewMaxChars = 10
        };

        var preview = AiPayloadRedactor.Preview("abcdefghijklmnopqrstuvwxyz", settings);

        Assert.Equal("abcdefghij…", preview);
    }

    [Fact]
    public void Hash_IsStableAndShort()
    {
        var hash1 = AiPayloadRedactor.Hash("same-input");
        var hash2 = AiPayloadRedactor.Hash("same-input");

        Assert.Equal(hash1, hash2);
        Assert.Equal(16, hash1.Length);
        Assert.NotEqual(AiPayloadRedactor.Hash("other"), hash1);
    }

    [Fact]
    public void CharCount_HandlesNull()
    {
        Assert.Equal(0, AiPayloadRedactor.CharCount(null));
        Assert.Equal(3, AiPayloadRedactor.CharCount("abc"));
    }
}

public class AiTelemetryOperationTests
{
    [Fact]
    public void NullTelemetry_StartOperation_DoesNotThrow()
    {
        var telemetry = NullAiTelemetry.Instance;
        using var scope = telemetry.StartOperation(AiTelemetryNames.Features.Classify, model: "test");
        scope.SetOutcome(AiTelemetryNames.Outcomes.Success);
        Assert.False(telemetry.IsEnabled);
        Assert.Null(scope.Activity);
    }

    [Fact]
    public void AiTelemetry_CompleteLlmCall_WithNullActivity_DoesNotThrow()
    {
        var config = new AiConfiguration
        {
            Provider = "ollama",
            Observability = new AiObservabilitySettings { Enabled = true, RecordTokenUsage = true }
        };
        var telemetry = new AiTelemetry(config, Microsoft.Extensions.Logging.Abstractions.NullLogger<AiTelemetry>.Instance);

        telemetry.CompleteLlmCall(
            activity: null,
            latencyMs: 12,
            outcome: AiTelemetryNames.Outcomes.Success,
            outputChars: 4,
            inputTokens: 10,
            outputTokens: 2,
            totalTokens: 12);
    }
}
