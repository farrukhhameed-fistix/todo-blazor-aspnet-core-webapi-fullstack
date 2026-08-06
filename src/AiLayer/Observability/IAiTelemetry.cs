#nullable enable

using System.Diagnostics;

namespace Fistix.TaskManager.AiLayer.Observability;

public interface IAiTelemetry
{
    bool IsEnabled { get; }

    AiOperationScope StartOperation(
        string feature,
        string? model = null,
        string? provider = null,
        Guid? todoExternalId = null,
        Guid? jobExternalId = null);

    Activity? StartLlmCall(
        string feature,
        string? model,
        string? provider,
        int? inputChars = null,
        string? inputPreview = null);

    void CompleteLlmCall(
        Activity? activity,
        long latencyMs,
        string outcome,
        int? outputChars = null,
        string? outputPreview = null,
        long? inputTokens = null,
        long? outputTokens = null,
        long? totalTokens = null);

    Activity? StartToolCall(string toolName, string? argsPreview = null);

    void CompleteToolCall(Activity? activity, long durationMs, bool success, string? error = null);

    void RecordOperationError(string feature, string outcome);

    void RecordQualityEvent(string feature, string eventName);

    void RecordOverrideDecision(string confidenceBand, bool wasOverridden);
}

/// <summary>Tracks end-to-end AI feature duration and outcome.</summary>
public sealed class AiOperationScope : IDisposable
{
    private readonly IAiTelemetry _telemetry;
    private readonly string _feature;
    private readonly Stopwatch _stopwatch;
    private bool _disposed;

    public Activity? Activity { get; }
    public string Outcome { get; set; } = AiTelemetryNames.Outcomes.Success;

    internal AiOperationScope(IAiTelemetry telemetry, string feature, Activity? activity)
    {
        _telemetry = telemetry;
        _feature = feature;
        Activity = activity;
        _stopwatch = Stopwatch.StartNew();
    }

    public void SetOutcome(string outcome) => Outcome = outcome;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _stopwatch.Stop();

        if (Activity is null)
        {
            return;
        }

        Activity.SetTag(AiTelemetryNames.Tags.RequestDurationMs, _stopwatch.ElapsedMilliseconds);
        Activity.SetTag(AiTelemetryNames.Tags.Outcome, Outcome);
        if (!string.Equals(Outcome, AiTelemetryNames.Outcomes.Success, StringComparison.Ordinal))
        {
            Activity.SetStatus(ActivityStatusCode.Error, Outcome);
            _telemetry.RecordOperationError(_feature, Outcome);
        }

        Activity.Dispose();
        AiTelemetry.RecordOperationDuration(_feature, _stopwatch.ElapsedMilliseconds, Outcome);
    }
}
