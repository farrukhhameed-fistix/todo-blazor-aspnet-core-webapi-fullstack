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
    public SpeechToTextSettings SpeechToText { get; set; } = new();
    public AiFeaturesConfiguration Features { get; set; } = new();
    public AiObservabilitySettings Observability { get; set; } = new();
}

/// <summary>
/// Local OpenAI-compatible STT sidecar (e.g. Speaches / faster-whisper).
/// Endpoint is the service base URL without a trailing slash (e.g. http://localhost:8000).
/// </summary>
public class SpeechToTextSettings
{
    public string Endpoint { get; set; } = "";

    /// <summary>Model id sent to /v1/audio/transcriptions (sidecar-specific).</summary>
    public string Model { get; set; } = "Systran/faster-whisper-tiny";

    /// <summary>
    /// Optional domain hint passed to STT service to improve transcription of project-specific terms.
    /// </summary>
    public string VocabularyPrompt { get; set; } = "";

    public int TimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// HttpClient timeout for STT (includes first-run model download). Default 10 minutes.
    /// </summary>
    public int WarmupTimeoutSeconds { get; set; } = 600;

    /// <summary>Max uploaded audio size in bytes (default 5 MiB).</summary>
    public int MaxAudioBytes { get; set; } = 5 * 1024 * 1024;

    /// <summary>
    /// When true, the client streams PCM batches and the hub runs one-in-flight Whisper for local live captions.
    /// Skips browser Web Speech. Default false uses WebM + Web Speech.
    /// </summary>
    public bool EnableLocalLiveCaptions { get; set; }

    /// <summary>ISO-639-1 hint sent to Speaches (empty = let the model detect).</summary>
    public string DecodeLanguage { get; set; } = "en";

    /// <summary>Target PCM sample rate when local live captions are enabled.</summary>
    public int PcmSampleRate { get; set; } = 16000;

    /// <summary>How often the browser flushes PCM to the hub (200–400 ms).</summary>
    public int LiveCaptionBatchMs { get; set; } = 300;

    public string[] AllowedContentTypes { get; set; } =
    [
        "audio/webm",
        "audio/pcm",
        "audio/l16",
        "audio/wav",
        "audio/x-wav",
        "audio/mpeg",
        "audio/mp4",
        "audio/ogg",
        "audio/flac",
        "application/octet-stream"
    ];
}

/// <summary>
/// OpenTelemetry GenAI observability controls. Payloads are redacted unless preview is enabled.
/// </summary>
public class AiObservabilitySettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>When true, truncated prompt/response previews may be attached to spans/logs.</summary>
    public bool CapturePayloadPreview { get; set; } = false;

    public int PayloadPreviewMaxChars { get; set; } = 256;

    /// <summary>Record provider token usage when available (often missing for Ollama).</summary>
    public bool RecordTokenUsage { get; set; } = true;
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

    /// <summary>Mark running sprint optimizer jobs stuck after this many seconds without heartbeat.</summary>
    public int StuckAfterSeconds { get; set; } = 300;

    /// <summary>Hard stop when sprint planning tools are invoked this many times in one job.</summary>
    public int MaxToolInvocationsPerJob { get; set; } = 12;

    /// <summary>Max Planner recovery re-runs after an empty selection (0 = no recovery).</summary>
    public int MaxPlannerRecoveryPasses { get; set; } = 1;

    /// <summary>Cancel a running sprint optimizer job after this many seconds.</summary>
    public int JobTimeoutSeconds { get; set; } = 240;
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
    public RagConfiguration Rag { get; set; } = new();
    public AiRateLimitConfiguration RagRateLimit { get; set; } = new();
    public bool EnableFunctionCalling { get; set; } = false;
    public AiRateLimitConfiguration FunctionCallingRateLimit { get; set; } = new();
    public bool EnableAgents { get; set; } = false;
    public AiRateLimitConfiguration AgentsRateLimit { get; set; } = new();
    public bool EnableVoiceTranscription { get; set; } = false;
    public AiRateLimitConfiguration VoiceTranscriptionRateLimit { get; set; } = new();
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
    /// Minimum cosine similarity (0–1) required to keep a hit on the vector-only path.
    /// Nearest-neighbor search always returns something; scores below this are treated as irrelevant.
    /// </summary>
    public double MinSimilarity { get; set; } = 0.45;

    /// <summary>When true, fuse vector + Postgres full-text with RRF (and light score blend).</summary>
    public bool HybridEnabled { get; set; } = false;

    /// <summary>How many vector neighbors to fetch before fusion when hybrid is on.</summary>
    public int VectorCandidateLimit { get; set; } = 40;

    /// <summary>How many FTS hits to fetch before fusion when hybrid is on.</summary>
    public int LexicalCandidateLimit { get; set; } = 40;

    /// <summary>RRF constant k (typical 60).</summary>
    public int RrfK { get; set; } = 60;
}

/// <summary>Ask / RAG retrieval knobs (filters and prompts live in ServiceLayer + RAGPipeline).</summary>
public class RagConfiguration
{
    /// <summary>Max todos passed into the LLM after structured filters + search.</summary>
    public int RetrievalLimit { get; set; } = 5;
}
