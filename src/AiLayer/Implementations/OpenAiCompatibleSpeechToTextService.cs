#nullable enable

using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Fistix.TaskManager.AiLayer.Abstractions;
using Fistix.TaskManager.AiLayer.Shared;
using Microsoft.Extensions.Logging;

namespace Fistix.TaskManager.AiLayer.Implementations;

/// <summary>
/// Calls an OpenAI-compatible local STT sidecar (Speaches / faster-whisper):
/// POST {Endpoint}/v1/audio/transcriptions
/// </summary>
public sealed class OpenAiCompatibleSpeechToTextService : ISpeechToTextService, ISpeechToTextModelWarmup
{
    public const int MaxTranscriptLength = 2000;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AiConfiguration _aiConfig;
    private readonly ILogger<OpenAiCompatibleSpeechToTextService> _logger;
    private readonly SemaphoreSlim _ensureLock = new(1, 1);

    private volatile bool _isReady;
    private volatile bool _isWarmingUp;
    private string? _lastError;

    public OpenAiCompatibleSpeechToTextService(
        IHttpClientFactory httpClientFactory,
        AiConfiguration aiConfig,
        ILogger<OpenAiCompatibleSpeechToTextService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _aiConfig = aiConfig;
        _logger = logger;
    }

    public bool IsReady => _isReady;
    public bool IsWarmingUp => _isWarmingUp;
    public string? LastError => _lastError;

    public void EnsureModelInBackground()
    {
        if (_isReady || _isWarmingUp)
        {
            return;
        }

        _ = Task.Run(() => EnsureModelAvailableAsync(CancellationToken.None));
    }

    public async Task<string> TranscribeAsync(
        Stream audioStream,
        string fileName,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        var settings = _aiConfig.SpeechToText;
        if (string.IsNullOrWhiteSpace(settings.Endpoint))
        {
            throw new InvalidOperationException("Speech-to-text endpoint is not configured.");
        }

        EnsureModelInBackground();
        if (!_isReady)
        {
            var detail = _lastError is null
                ? "Speech model is preparing. Please retry shortly."
                : $"Speech model is not ready yet: {_lastError}";
            throw new SpeechToTextUnavailableException(detail, 15);
        }

        ArgumentNullException.ThrowIfNull(audioStream);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = "audio.webm";
        }

        if (string.IsNullOrWhiteSpace(contentType))
        {
            contentType = "application/octet-stream";
        }

        using var content = new MultipartFormDataContent();
        var streamContent = new StreamContent(audioStream);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(streamContent, "file", fileName);

        if (!string.IsNullOrWhiteSpace(settings.Model))
        {
            content.Add(new StringContent(settings.Model), "model");
        }

        var baseUrl = settings.Endpoint.TrimEnd('/');
        var url = $"{baseUrl}/v1/audio/transcriptions";

        _logger.LogInformation("Transcribing audio via {Url} (file {FileName})", url, fileName);

        var client = _httpClientFactory.CreateClient("speech-to-text");
        using var response = await client.PostAsync(url, content, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Speech-to-text failed with {StatusCode}: {Body}",
                (int)response.StatusCode,
                TruncateForLog(body));

            if (response.StatusCode == HttpStatusCode.NotFound && IsMissingModel(body))
            {
                _isReady = false;
                EnsureModelInBackground();
                throw new SpeechToTextUnavailableException("Speech model is downloading. Please retry shortly.", 20);
            }

            throw new InvalidOperationException("Speech-to-text service failed to transcribe audio.");
        }

        var transcript = ExtractTranscript(body);
        return PromptInputSanitizer.SanitizeAndTruncate(transcript, MaxTranscriptLength);
    }

    private async Task EnsureModelAvailableAsync(CancellationToken cancellationToken)
    {
        if (_isReady)
        {
            return;
        }

        await _ensureLock.WaitAsync(cancellationToken);
        try
        {
            if (_isReady)
            {
                return;
            }

            _isWarmingUp = true;
            _lastError = null;

            var settings = _aiConfig.SpeechToText;
            if (string.IsNullOrWhiteSpace(settings.Endpoint) || string.IsNullOrWhiteSpace(settings.Model))
            {
                _lastError = "Speech endpoint or model is not configured.";
                return;
            }

            var baseUrl = settings.Endpoint.TrimEnd('/');
            var client = _httpClientFactory.CreateClient("speech-to-text");

            if (await IsModelInstalledAsync(client, baseUrl, settings.Model, cancellationToken))
            {
                _logger.LogInformation("Speech model {Model} already installed — skipping download", settings.Model);
                _isReady = true;
                _lastError = null;
                return;
            }

            var modelUrl = $"{baseUrl}/v1/models/{Uri.EscapeDataString(settings.Model)}";
            _logger.LogInformation("Downloading speech model {Model} from {Url}", settings.Model, modelUrl);
            using var downloadResponse = await client.PostAsync(modelUrl, content: null, cancellationToken);
            if (downloadResponse.IsSuccessStatusCode)
            {
                _logger.LogInformation("Speech model {Model} download completed", settings.Model);
                _isReady = true;
                _lastError = null;
                return;
            }

            var body = await downloadResponse.Content.ReadAsStringAsync(cancellationToken);
            // Speaches may return an error if another process already downloaded it — re-check list.
            if (await IsModelInstalledAsync(client, baseUrl, settings.Model, cancellationToken))
            {
                _logger.LogInformation("Speech model {Model} became available after download attempt", settings.Model);
                _isReady = true;
                _lastError = null;
                return;
            }

            _lastError = $"Model download failed: {(int)downloadResponse.StatusCode} {TruncateForLog(body)}";
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            _logger.LogWarning(ex, "Speech model warmup failed");
        }
        finally
        {
            _isWarmingUp = false;
            _ensureLock.Release();
        }
    }

    private static async Task<bool> IsModelInstalledAsync(
        HttpClient client,
        string baseUrl,
        string modelId,
        CancellationToken cancellationToken)
    {
        // Prefer list endpoint — more reliable than GET by id with encoded slashes.
        using var listResponse = await client.GetAsync(
            $"{baseUrl}/v1/models?task=automatic-speech-recognition",
            cancellationToken);

        if (listResponse.IsSuccessStatusCode)
        {
            var listBody = await listResponse.Content.ReadAsStringAsync(cancellationToken);
            if (ListContainsModel(listBody, modelId))
            {
                return true;
            }
        }

        using var getResponse = await client.GetAsync(
            $"{baseUrl}/v1/models/{Uri.EscapeDataString(modelId)}",
            cancellationToken);
        return getResponse.IsSuccessStatusCode;
    }

    private static bool ListContainsModel(string json, string modelId)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (var item in data.EnumerateArray())
            {
                if (item.TryGetProperty("id", out var idProp)
                    && string.Equals(idProp.GetString(), modelId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch (JsonException)
        {
            return false;
        }

        return false;
    }

    private static bool IsMissingModel(string body) =>
        body.Contains("not installed", StringComparison.OrdinalIgnoreCase)
        || body.Contains("model", StringComparison.OrdinalIgnoreCase) && body.Contains("download", StringComparison.OrdinalIgnoreCase);

    private static string ExtractTranscript(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return string.Empty;
        }

        var trimmed = body.Trim();
        if (trimmed.StartsWith('{'))
        {
            using var doc = JsonDocument.Parse(trimmed);
            if (doc.RootElement.TryGetProperty("text", out var textProp))
            {
                return textProp.GetString() ?? string.Empty;
            }
        }

        return trimmed;
    }

    private static string TruncateForLog(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Length <= 200 ? value : value[..200];
    }
}
