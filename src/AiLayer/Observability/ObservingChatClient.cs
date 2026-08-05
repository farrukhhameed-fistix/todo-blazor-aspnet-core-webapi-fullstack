#nullable enable

using System.Diagnostics;
using Fistix.TaskManager.AiLayer.Shared;
using Microsoft.Extensions.AI;

namespace Fistix.TaskManager.AiLayer.Observability;

/// <summary>Decorates MAF <see cref="IChatClient"/> with GenAI latency and usage spans.</summary>
public sealed class ObservingChatClient : DelegatingChatClient
{
    private readonly IAiTelemetry _telemetry;
    private readonly AiConfiguration _aiConfig;
    private readonly string _model;

    public ObservingChatClient(
        IChatClient innerClient,
        IAiTelemetry telemetry,
        AiConfiguration aiConfig,
        string model)
        : base(innerClient)
    {
        _telemetry = telemetry;
        _aiConfig = aiConfig;
        _model = model;
    }

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var inputText = string.Join('\n', messages.Select(m => m.Text ?? string.Empty));
        var activity = _telemetry.StartLlmCall(
            AiTelemetryNames.Features.Chat,
            options?.ModelId ?? _model,
            _aiConfig.Provider,
            AiPayloadRedactor.CharCount(inputText),
            inputText);

        var sw = Stopwatch.StartNew();
        try
        {
            var response = await base.GetResponseAsync(messages, options, cancellationToken);
            sw.Stop();

            long? inputTokens = null;
            long? outputTokens = null;
            long? totalTokens = null;
            if (_aiConfig.Observability.RecordTokenUsage && response.Usage is { } usage)
            {
                inputTokens = usage.InputTokenCount;
                outputTokens = usage.OutputTokenCount;
                totalTokens = usage.TotalTokenCount;
            }

            _telemetry.CompleteLlmCall(
                activity,
                sw.ElapsedMilliseconds,
                AiTelemetryNames.Outcomes.Success,
                AiPayloadRedactor.CharCount(response.Text),
                response.Text,
                inputTokens,
                outputTokens,
                totalTokens);

            return response;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            sw.Stop();
            _telemetry.CompleteLlmCall(activity, sw.ElapsedMilliseconds, AiTelemetryNames.Outcomes.Cancelled);
            throw;
        }
        catch (Exception)
        {
            sw.Stop();
            _telemetry.CompleteLlmCall(activity, sw.ElapsedMilliseconds, AiTelemetryNames.Outcomes.Error);
            throw;
        }
    }

    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        // Agents use non-streaming GetResponseAsync; pass through without buffering.
        base.GetStreamingResponseAsync(messages, options, cancellationToken);
}
