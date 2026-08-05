#nullable enable

using Fistix.TaskManager.AiLayer.Shared;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OpenAI;
using System.ClientModel;
using Fistix.TaskManager.AiLayer.Observability;

namespace Fistix.TaskManager.AiLayer.Agents;

/// <summary>
/// Builds an <see cref="IChatClient"/> for Microsoft Agent Framework from Ai configuration.
/// Google Gemini uses the OpenAI-compatible Generative Language endpoint so tool calling works.
/// </summary>
public sealed class AiChatClientFactory
{
    private readonly AiConfiguration _aiConfig;
    private readonly IAiTelemetry _telemetry;
    private readonly ILogger<AiChatClientFactory> _logger;

    public AiChatClientFactory(
        AiConfiguration aiConfig,
        ILogger<AiChatClientFactory> logger,
        IAiTelemetry? telemetry = null)
    {
        _aiConfig = aiConfig;
        _logger = logger;
        _telemetry = telemetry ?? NullAiTelemetry.Instance;
    }

    public IChatClient CreateChatClient()
    {
        var provider = (_aiConfig.Provider ?? "openai").Trim().ToLowerInvariant();
        var (inner, model) = provider switch
        {
            "google" => CreateGoogleOpenAiCompatibleClient(),
            "azureopenai" => CreateAzureOpenAiClient(),
            "ollama" => CreateOllamaClient(),
            "claude" => throw new InvalidOperationException(
                "Microsoft Agent Framework sprint agent does not support Claude directly yet. Use Provider google, openai, azureopenai, or ollama."),
            _ => CreateOpenAiClient()
        };

        return new ObservingChatClient(inner, _telemetry, _aiConfig, model);
    }

    private (IChatClient Client, string Model) CreateOpenAiClient()
    {
        var apiKey = ResolveApiKey(_aiConfig.OpenAI.ApiKey);
        var model = ResolveModelOverride(_aiConfig.OpenAI.Model, "gpt-4o-mini");
        _logger.LogInformation("MAF chat client using OpenAI model {Model}", model);
        return (new OpenAIClient(apiKey).GetChatClient(model).AsIChatClient(), model);
    }

    private (IChatClient Client, string Model) CreateAzureOpenAiClient()
    {
        var apiKey = ResolveApiKey(_aiConfig.AzureOpenAI.ApiKey);
        var endpoint = _aiConfig.AzureOpenAI.Endpoint?.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            throw new InvalidOperationException("Ai:AzureOpenAI:Endpoint is required for Azure OpenAI agents.");
        }

        var model = ResolveModelOverride(_aiConfig.AzureOpenAI.Model, "gpt-4o");
        var options = new OpenAIClientOptions { Endpoint = new Uri($"{endpoint}/openai/v1") };
        _logger.LogInformation("MAF chat client using Azure OpenAI deployment {Model}", model);
        return (new OpenAIClient(new ApiKeyCredential(apiKey), options).GetChatClient(model).AsIChatClient(), model);
    }

    private (IChatClient Client, string Model) CreateOllamaClient()
    {
        var endpoint = string.IsNullOrWhiteSpace(_aiConfig.Ollama.Endpoint)
            ? "http://localhost:11434"
            : _aiConfig.Ollama.Endpoint.TrimEnd('/');
        if (!endpoint.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
        {
            endpoint += "/v1";
        }

        var model = ResolveModelOverride(_aiConfig.Ollama.Model, "mistral:7b");
        var options = new OpenAIClientOptions { Endpoint = new Uri(endpoint) };
        _logger.LogInformation("MAF chat client using Ollama model {Model} at {Endpoint}", model, endpoint);
        return (new OpenAIClient(new ApiKeyCredential("ollama"), options).GetChatClient(model).AsIChatClient(), model);
    }

    private (IChatClient Client, string Model) CreateGoogleOpenAiCompatibleClient()
    {
        var apiKey = ResolveApiKey(_aiConfig.GoogleAI.ApiKey);
        var model = ResolveGoogleMafModel();
        var options = new OpenAIClientOptions
        {
            Endpoint = new Uri("https://generativelanguage.googleapis.com/v1beta/openai/")
        };
        _logger.LogInformation("MAF chat client using Google OpenAI-compatible model {Model}", model);
        return (new OpenAIClient(new ApiKeyCredential(apiKey), options).GetChatClient(model).AsIChatClient(), model);
    }

    /// <summary>
    /// Gemini 3.x via OpenAI-compat often returns 400 on tool-call follow-ups because thought/reasoning
    /// signatures are not preserved by the OpenAI client. Prefer an explicit Agents:ChatModel, else
    /// a non-3.x Google model (fallback list or gemini-2.5-flash).
    /// </summary>
    private string ResolveGoogleMafModel()
    {
        if (!string.IsNullOrWhiteSpace(_aiConfig.Agents.ChatModel))
        {
            return _aiConfig.Agents.ChatModel.Trim();
        }

        var primary = string.IsNullOrWhiteSpace(_aiConfig.GoogleAI.Model)
            ? "gemini-2.5-flash"
            : _aiConfig.GoogleAI.Model.Trim();

        if (!IsGemini3Family(primary))
        {
            return primary;
        }

        var safer = (_aiConfig.GoogleAI.FallbackModels ?? [])
            .Select(m => m?.Trim())
            .FirstOrDefault(m => !string.IsNullOrWhiteSpace(m) && !IsGemini3Family(m!));

        safer ??= "gemini-2.5-flash";
        _logger.LogWarning(
            "Ai:GoogleAI:Model '{Primary}' is Gemini 3.x; MAF OpenAI-compat tool calling often fails with HTTP 400. Using '{Safer}' for agents. Set Ai:Agents:ChatModel to override.",
            primary,
            safer);
        return safer;
    }

    private string ResolveModelOverride(string configured, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(_aiConfig.Agents.ChatModel))
        {
            return _aiConfig.Agents.ChatModel.Trim();
        }

        return string.IsNullOrWhiteSpace(configured) ? fallback : configured.Trim();
    }

    private static bool IsGemini3Family(string model) =>
        model.StartsWith("gemini-3", StringComparison.OrdinalIgnoreCase);

    private static string ResolveApiKey(string configuredValue)
    {
        if (string.IsNullOrWhiteSpace(configuredValue))
        {
            throw new InvalidOperationException("API key is not configured for the active AI provider.");
        }

        if (configuredValue.StartsWith("${", StringComparison.Ordinal) && configuredValue.EndsWith('}'))
        {
            var envName = configuredValue[2..^1];
            return Environment.GetEnvironmentVariable(envName)
                ?? throw new InvalidOperationException($"Environment variable '{envName}' is not set.");
        }

        return configuredValue;
    }
}
