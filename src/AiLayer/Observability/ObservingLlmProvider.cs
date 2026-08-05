#nullable enable

using System.Diagnostics;
using Fistix.TaskManager.AiLayer.Abstractions;
using Fistix.TaskManager.AiLayer.Shared;

namespace Fistix.TaskManager.AiLayer.Observability;

/// <summary>Decorates <see cref="ILlmProviderService"/> with LLM latency/token spans.</summary>
public sealed class ObservingLlmProvider : ILlmProviderService
{
    private readonly ILlmProviderService _inner;
    private readonly IAiTelemetry _telemetry;
    private readonly AiConfiguration _aiConfig;

    public ObservingLlmProvider(
        ILlmProviderService inner,
        IAiTelemetry telemetry,
        AiConfiguration aiConfig)
    {
        _inner = inner;
        _telemetry = telemetry;
        _aiConfig = aiConfig;
    }

    public async Task<string> GetCompletionAsync(string prompt, CancellationToken cancellationToken = default)
    {
        var model = ResolveModel();
        var feature = AiTelemetryNames.Features.LlmCompletion;
        var activity = _telemetry.StartLlmCall(
            feature,
            model,
            _aiConfig.Provider,
            AiPayloadRedactor.CharCount(prompt),
            prompt);

        var sw = Stopwatch.StartNew();
        try
        {
            var response = await _inner.GetCompletionAsync(prompt, cancellationToken);
            sw.Stop();
            _telemetry.CompleteLlmCall(
                activity,
                sw.ElapsedMilliseconds,
                AiTelemetryNames.Outcomes.Success,
                AiPayloadRedactor.CharCount(response),
                response);
            return response;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            sw.Stop();
            _telemetry.CompleteLlmCall(
                activity,
                sw.ElapsedMilliseconds,
                AiTelemetryNames.Outcomes.Cancelled);
            throw;
        }
        catch (Exception)
        {
            sw.Stop();
            _telemetry.CompleteLlmCall(
                activity,
                sw.ElapsedMilliseconds,
                AiTelemetryNames.Outcomes.Error);
            throw;
        }
    }

    private string ResolveModel() =>
        _aiConfig.Provider.ToLowerInvariant() switch
        {
            "google" => _aiConfig.GoogleAI.Model,
            "azureopenai" => _aiConfig.AzureOpenAI.Model,
            "ollama" => _aiConfig.Ollama.Model,
            "claude" => _aiConfig.Claude.Model,
            _ => _aiConfig.OpenAI.Model
        };
}
