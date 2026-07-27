namespace Fistix.TaskManager.AiLayer.Shared;

/// <summary>
/// Configuration settings for AI features and LLM providers.
/// </summary>
public class AiConfiguration
{
    public string Provider { get; set; } = "OpenAI";

    public OpenAiSettings OpenAI { get; set; } = new();
    public AzureOpenAISettings AzureOpenAI { get; set; } = new();
    public OllamaSettings Ollama { get; set; } = new();
    public GoogleAISettings GoogleAI { get; set; } = new();
    public ClaudeSettings Claude { get; set; } = new();
    public EmbeddingSettings Embedding { get; set; } = new();
    public AgentsSettings Agents { get; set; } = new();
    public AiFeaturesConfiguration Features { get; set; } = new();
}

/// <summary>
/// Microsoft Agent Framework / tool-calling agent settings.
/// ChatModel overrides the provider default when set (recommended for Gemini 3 + OpenAI-compat).
/// </summary>
public class AgentsSettings
{
    /// <summary>Optional chat model for MAF agents only (does not change classify/summarize/RAG).</summary>
    public string ChatModel { get; set; } = "";

    /// <summary>
    /// <c>Multi</c> = Analyst → Planner sequential workflow (default).
    /// <c>Single</c> = one agent with all tools.
    /// </summary>
    public string WorkflowMode { get; set; } = "Multi";
}

public class EmbeddingSettings
{
    /// <summary>Onnx (local BGE), OpenAI, or Ollama.</summary>
    public string Provider { get; set; } = "Onnx";
    public string Model { get; set; } = "bge-small-en-v1.5";
    public int Dimension { get; set; } = 384;
    public string ApiKey { get; set; } = "";
    public string Endpoint { get; set; } = "https://api.openai.com/v1";
    public OnnxEmbeddingSettings Onnx { get; set; } = new();
}

public class OnnxEmbeddingSettings
{
    /// <summary>Directory containing model.onnx and vocab.txt (relative to content root or absolute).</summary>
    public string ModelDirectory { get; set; } = "models/bge-small-en-v1.5";
    public int MaxSequenceLength { get; set; } = 512;
    public string QueryInstruction { get; set; } = "Represent this sentence for searching: ";
}

public class OpenAiSettings
{
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "gpt-4o-mini";
    public string Endpoint { get; set; } = "https://api.openai.com/v1";
}

public class AzureOpenAISettings
{
    public string ApiKey { get; set; } = "";
    public string Endpoint { get; set; } = "";
    public string Model { get; set; } = "gpt-4";
}

public class OllamaSettings
{
    public string Endpoint { get; set; } = "http://localhost:11434";
    public string Model { get; set; } = "mistral:7b";
}

public class GoogleAISettings
{
    // API key (supports environment variable reference pattern: ${ENV_VAR})
    public string ApiKey { get; set; } = "";

    // Optional path to a service account JSON file when using Vertex/GenAI with service-account credentials
    public string ServiceAccountJsonPath { get; set; } = "";

    public string Model { get; set; } = "";

    // V1 or V1_Beta — most Gemini chat models require V1_Beta
    public string ApiVersion { get; set; } = "";

    // Used when the primary model returns transient errors (503/429)
    public string[] FallbackModels { get; set; } = [];
}

public class ClaudeSettings
{
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "claude-3-5-sonnet-latest";
    public string Endpoint { get; set; } = "https://api.anthropic.com";
}

public class AiFeaturesConfiguration
{
    public bool EnableSummarization { get; set; } = true;
    public AiRateLimitConfiguration SummarizeRateLimit { get; set; } = new();
    public bool EnableClassification { get; set; } = false;
    public AiRateLimitConfiguration ClassifyRateLimit { get; set; } = new();
    public ClassificationConfiguration Classification { get; set; } = new();
    public bool EnableEmbeddings { get; set; } = false;
    public bool EnableSemanticSearch { get; set; } = false;
    public SemanticSearchConfiguration SemanticSearch { get; set; } = new();
    public AiRateLimitConfiguration SemanticSearchRateLimit { get; set; } = new();
    public bool EnableRag { get; set; } = false;
    public AiRateLimitConfiguration RagRateLimit { get; set; } = new();
    public bool EnableFunctionCalling { get; set; } = false;
    public AiRateLimitConfiguration FunctionCallingRateLimit { get; set; } = new();
    public bool EnableAgents { get; set; } = false;
    public AiRateLimitConfiguration AgentsRateLimit { get; set; } = new();
    public bool EnableMcp { get; set; } = false;

    public AiBatchConfiguration Batch { get; set; } = new();
}

public class AiBatchConfiguration
{
    public int StuckAfterSeconds { get; set; } = 120;
    public int ItemTimeoutMs { get; set; } = 90_000;
    public int DefaultBatchSize { get; set; } = 5;
    public int DefaultDelayMsBetweenItems { get; set; } = 500;
}

public class SemanticSearchConfiguration
{
    /// <summary>
    /// Minimum cosine similarity (0–1) required to keep a hit.
    /// Nearest-neighbor search always returns something; scores below this are treated as irrelevant.
    /// </summary>
    public double MinSimilarity { get; set; } = 0.45;
}
