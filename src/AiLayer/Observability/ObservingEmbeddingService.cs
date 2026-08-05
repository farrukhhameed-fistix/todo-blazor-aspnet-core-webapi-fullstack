#nullable enable

using System.Diagnostics;
using Fistix.TaskManager.AiLayer.Abstractions;

namespace Fistix.TaskManager.AiLayer.Observability;

/// <summary>Decorates embedding generation with duration and model attributes.</summary>
public sealed class ObservingEmbeddingService : IEmbeddingService
{
    private readonly IEmbeddingService _inner;
    private readonly IAiTelemetry _telemetry;

    public ObservingEmbeddingService(IEmbeddingService inner, IAiTelemetry telemetry)
    {
        _inner = inner;
        _telemetry = telemetry;
    }

    public string ModelName => _inner.ModelName;
    public int Dimension => _inner.Dimension;

    public async Task<float[]> GenerateEmbeddingAsync(
        string text,
        EmbeddingInputKind kind = EmbeddingInputKind.Passage,
        CancellationToken cancellationToken = default)
    {
        using var scope = _telemetry.StartOperation(
            AiTelemetryNames.Features.Embed,
            model: ModelName);

        scope.Activity?.SetTag(AiTelemetryNames.Tags.EmbeddingDimension, Dimension);
        scope.Activity?.SetTag(AiTelemetryNames.Tags.InputChars, AiPayloadRedactor.CharCount(text));
        scope.Activity?.SetTag("ai.embedding.kind", kind.ToString());

        var sw = Stopwatch.StartNew();
        var activity = _telemetry.StartLlmCall(
            AiTelemetryNames.Features.Embed,
            ModelName,
            provider: null,
            AiPayloadRedactor.CharCount(text),
            text);

        try
        {
            var embedding = await _inner.GenerateEmbeddingAsync(text, kind, cancellationToken);
            sw.Stop();
            _telemetry.CompleteLlmCall(
                activity,
                sw.ElapsedMilliseconds,
                AiTelemetryNames.Outcomes.Success,
                outputChars: embedding.Length);
            return embedding;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            sw.Stop();
            scope.SetOutcome(AiTelemetryNames.Outcomes.Cancelled);
            _telemetry.CompleteLlmCall(activity, sw.ElapsedMilliseconds, AiTelemetryNames.Outcomes.Cancelled);
            throw;
        }
        catch (Exception)
        {
            sw.Stop();
            scope.SetOutcome(AiTelemetryNames.Outcomes.Error);
            _telemetry.CompleteLlmCall(activity, sw.ElapsedMilliseconds, AiTelemetryNames.Outcomes.Error);
            throw;
        }
    }
}
