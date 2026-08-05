#nullable enable

using System.Diagnostics;
using System.Diagnostics.Metrics;
using Fistix.TaskManager.AiLayer.Shared;
using Microsoft.Extensions.Logging;

namespace Fistix.TaskManager.AiLayer.Observability;

public sealed class AiTelemetry : IAiTelemetry
{
    private static readonly ActivitySource ActivitySource = new(AiTelemetryNames.ActivitySourceName);
    private static readonly Meter Meter = new(AiTelemetryNames.MeterName);

    private static readonly Histogram<double> LlmDuration = Meter.CreateHistogram<double>(
        AiTelemetryNames.LlmDurationInstrument,
        unit: "ms",
        description: "LLM provider round-trip duration");

    private static readonly Histogram<long> LlmTokens = Meter.CreateHistogram<long>(
        AiTelemetryNames.LlmTokensInstrument,
        unit: "{token}",
        description: "LLM token usage when reported by the provider");

    private static readonly Histogram<double> ToolDuration = Meter.CreateHistogram<double>(
        AiTelemetryNames.ToolDurationInstrument,
        unit: "ms",
        description: "AI tool call duration");

    private static readonly Histogram<double> OperationDuration = Meter.CreateHistogram<double>(
        AiTelemetryNames.OperationDurationInstrument,
        unit: "ms",
        description: "End-to-end AI feature duration");

    private static readonly Counter<long> OperationErrors = Meter.CreateCounter<long>(
        AiTelemetryNames.OperationErrorsInstrument,
        description: "AI feature errors by outcome");

    private readonly AiConfiguration _aiConfig;
    private readonly ILogger<AiTelemetry> _logger;

    public AiTelemetry(AiConfiguration aiConfig, ILogger<AiTelemetry> logger)
    {
        _aiConfig = aiConfig;
        _logger = logger;
    }

    public bool IsEnabled => _aiConfig.Observability?.Enabled ?? true;

    private AiObservabilitySettings Settings => _aiConfig.Observability ?? new AiObservabilitySettings();

    public AiOperationScope StartOperation(
        string feature,
        string? model = null,
        string? provider = null,
        Guid? todoExternalId = null,
        Guid? jobExternalId = null)
    {
        Activity? activity = null;
        if (IsEnabled)
        {
            activity = ActivitySource.StartActivity($"ai.operation/{feature}", ActivityKind.Internal);
            if (activity is not null)
            {
                activity.SetTag(AiTelemetryNames.Tags.Feature, feature);
                activity.SetTag(AiTelemetryNames.Tags.OperationName, feature);
                activity.SetTag(AiTelemetryNames.Tags.Provider, provider ?? _aiConfig.Provider);
                activity.SetTag(AiTelemetryNames.Tags.System, NormalizeSystem(provider ?? _aiConfig.Provider));
                if (!string.IsNullOrWhiteSpace(model))
                {
                    activity.SetTag(AiTelemetryNames.Tags.RequestModel, model);
                }

                if (todoExternalId.HasValue)
                {
                    activity.SetTag(AiTelemetryNames.Tags.TodoExternalId, todoExternalId.Value.ToString());
                }

                if (jobExternalId.HasValue)
                {
                    activity.SetTag(AiTelemetryNames.Tags.JobId, jobExternalId.Value.ToString());
                }
            }
        }

        return new AiOperationScope(this, feature, activity);
    }

    public Activity? StartLlmCall(
        string feature,
        string? model,
        string? provider,
        int? inputChars = null,
        string? inputPreview = null)
    {
        if (!IsEnabled)
        {
            return null;
        }

        var activity = ActivitySource.StartActivity($"ai.llm/{feature}", ActivityKind.Client);
        if (activity is null)
        {
            return null;
        }

        activity.SetTag(AiTelemetryNames.Tags.Feature, feature);
        activity.SetTag(AiTelemetryNames.Tags.OperationName, "chat");
        activity.SetTag(AiTelemetryNames.Tags.Provider, provider ?? _aiConfig.Provider);
        activity.SetTag(AiTelemetryNames.Tags.System, NormalizeSystem(provider ?? _aiConfig.Provider));
        if (!string.IsNullOrWhiteSpace(model))
        {
            activity.SetTag(AiTelemetryNames.Tags.RequestModel, model);
        }

        if (inputChars.HasValue)
        {
            activity.SetTag(AiTelemetryNames.Tags.InputChars, inputChars.Value);
        }

        var preview = AiPayloadRedactor.Preview(inputPreview, Settings);
        if (preview is not null)
        {
            activity.SetTag(AiTelemetryNames.Tags.InputPreview, preview);
        }

        return activity;
    }

    public void CompleteLlmCall(
        Activity? activity,
        long latencyMs,
        string outcome,
        int? outputChars = null,
        string? outputPreview = null,
        long? inputTokens = null,
        long? outputTokens = null,
        long? totalTokens = null)
    {
        if (!IsEnabled && activity is null)
        {
            return;
        }

        var feature = activity?.GetTagItem(AiTelemetryNames.Tags.Feature)?.ToString() ?? "unknown";
        var model = activity?.GetTagItem(AiTelemetryNames.Tags.RequestModel)?.ToString() ?? "unknown";
        var provider = activity?.GetTagItem(AiTelemetryNames.Tags.Provider)?.ToString() ?? _aiConfig.Provider;

        LlmDuration.Record(
            latencyMs,
            new KeyValuePair<string, object?>("feature", feature),
            new KeyValuePair<string, object?>("model", model),
            new KeyValuePair<string, object?>("outcome", outcome));

        if (Settings.RecordTokenUsage)
        {
            if (inputTokens.HasValue)
            {
                LlmTokens.Record(
                    inputTokens.Value,
                    new KeyValuePair<string, object?>("feature", feature),
                    new KeyValuePair<string, object?>("token_type", "input"));
            }

            if (outputTokens.HasValue)
            {
                LlmTokens.Record(
                    outputTokens.Value,
                    new KeyValuePair<string, object?>("feature", feature),
                    new KeyValuePair<string, object?>("token_type", "output"));
            }

            if (totalTokens.HasValue)
            {
                LlmTokens.Record(
                    totalTokens.Value,
                    new KeyValuePair<string, object?>("feature", feature),
                    new KeyValuePair<string, object?>("token_type", "total"));
            }
        }

        if (activity is not null)
        {
            activity.SetTag(AiTelemetryNames.Tags.LatencyMs, latencyMs);
            activity.SetTag(AiTelemetryNames.Tags.Outcome, outcome);
            if (outputChars.HasValue)
            {
                activity.SetTag(AiTelemetryNames.Tags.OutputChars, outputChars.Value);
            }

            var preview = AiPayloadRedactor.Preview(outputPreview, Settings);
            if (preview is not null)
            {
                activity.SetTag(AiTelemetryNames.Tags.OutputPreview, preview);
            }

            if (Settings.RecordTokenUsage)
            {
                if (inputTokens.HasValue)
                {
                    activity.SetTag(AiTelemetryNames.Tags.InputTokens, inputTokens.Value);
                }

                if (outputTokens.HasValue)
                {
                    activity.SetTag(AiTelemetryNames.Tags.OutputTokens, outputTokens.Value);
                }

                if (totalTokens.HasValue)
                {
                    activity.SetTag(AiTelemetryNames.Tags.TotalTokens, totalTokens.Value);
                }
            }

            if (!string.Equals(outcome, AiTelemetryNames.Outcomes.Success, StringComparison.Ordinal))
            {
                activity.SetStatus(ActivityStatusCode.Error, outcome);
            }

            activity.Dispose();
        }

        _logger.LogInformation(
            "AI LLM call feature={Feature} provider={Provider} model={Model} latencyMs={LatencyMs} outcome={Outcome} inputTokens={InputTokens} outputTokens={OutputTokens}",
            feature,
            provider,
            model,
            latencyMs,
            outcome,
            inputTokens,
            outputTokens);
    }

    public Activity? StartToolCall(string toolName, string? argsPreview = null)
    {
        if (!IsEnabled)
        {
            return null;
        }

        var activity = ActivitySource.StartActivity($"ai.tool/{toolName}", ActivityKind.Internal);
        if (activity is null)
        {
            return null;
        }

        activity.SetTag(AiTelemetryNames.Tags.ToolName, toolName);
        var preview = AiPayloadRedactor.Preview(argsPreview, Settings);
        if (preview is not null)
        {
            activity.SetTag(AiTelemetryNames.Tags.ToolArgsPreview, preview);
        }

        return activity;
    }

    public void CompleteToolCall(Activity? activity, long durationMs, bool success, string? error = null)
    {
        var toolName = activity?.GetTagItem(AiTelemetryNames.Tags.ToolName)?.ToString() ?? "unknown";
        ToolDuration.Record(
            durationMs,
            new KeyValuePair<string, object?>("tool", toolName),
            new KeyValuePair<string, object?>("success", success));

        if (activity is not null)
        {
            activity.SetTag(AiTelemetryNames.Tags.LatencyMs, durationMs);
            activity.SetTag(AiTelemetryNames.Tags.Outcome, success
                ? AiTelemetryNames.Outcomes.Success
                : AiTelemetryNames.Outcomes.Error);
            if (!success)
            {
                activity.SetStatus(ActivityStatusCode.Error, error ?? "tool_failed");
            }

            activity.Dispose();
        }
    }

    public void RecordOperationError(string feature, string outcome) =>
        OperationErrors.Add(
            1,
            new KeyValuePair<string, object?>("feature", feature),
            new KeyValuePair<string, object?>("outcome", outcome));

    internal static void RecordOperationDuration(string feature, long durationMs, string outcome) =>
        OperationDuration.Record(
            durationMs,
            new KeyValuePair<string, object?>("feature", feature),
            new KeyValuePair<string, object?>("outcome", outcome));

    private static string NormalizeSystem(string? provider) =>
        (provider ?? "unknown").Trim().ToLowerInvariant() switch
        {
            "openai" => "openai",
            "azureopenai" => "az.ai.openai",
            "ollama" => "ollama",
            "google" => "gcp.gen_ai",
            "claude" or "anthropic" => "anthropic",
            _ => provider?.Trim().ToLowerInvariant() ?? "unknown"
        };
}

/// <summary>No-op telemetry for unit tests and when DI is unavailable.</summary>
public sealed class NullAiTelemetry : IAiTelemetry
{
    public static readonly NullAiTelemetry Instance = new();

    public bool IsEnabled => false;

    public AiOperationScope StartOperation(
        string feature,
        string? model = null,
        string? provider = null,
        Guid? todoExternalId = null,
        Guid? jobExternalId = null) =>
        new(this, feature, activity: null);

    public Activity? StartLlmCall(
        string feature,
        string? model,
        string? provider,
        int? inputChars = null,
        string? inputPreview = null) => null;

    public void CompleteLlmCall(
        Activity? activity,
        long latencyMs,
        string outcome,
        int? outputChars = null,
        string? outputPreview = null,
        long? inputTokens = null,
        long? outputTokens = null,
        long? totalTokens = null)
    {
    }

    public Activity? StartToolCall(string toolName, string? argsPreview = null) => null;

    public void CompleteToolCall(Activity? activity, long durationMs, bool success, string? error = null)
    {
    }

    public void RecordOperationError(string feature, string outcome)
    {
    }
}
