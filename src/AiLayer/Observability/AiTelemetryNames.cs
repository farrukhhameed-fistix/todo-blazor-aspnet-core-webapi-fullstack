#nullable enable

namespace Fistix.TaskManager.AiLayer.Observability;

/// <summary>Shared OpenTelemetry names for AI spans and metrics (must match ServiceDefaults registration).</summary>
public static class AiTelemetryNames
{
    public const string ActivitySourceName = "TaskManager.Ai";
    public const string MeterName = "TaskManager.Ai";

    public const string LlmDurationInstrument = "ai.llm.duration";
    public const string LlmTokensInstrument = "ai.llm.tokens";
    public const string ToolDurationInstrument = "ai.tool.duration";
    public const string OperationDurationInstrument = "ai.operation.duration";
    public const string OperationErrorsInstrument = "ai.operation.errors";

    public static class Features
    {
        public const string Classify = "classify";
        public const string Summarize = "summarize";
        public const string Rag = "rag";
        public const string ProposeTools = "propose_tools";
        public const string ExecuteTools = "execute_tools";
        public const string SprintOptimizer = "sprint_optimizer";
        public const string Embed = "embed";
        public const string SemanticSearch = "semantic_search";
        public const string LlmCompletion = "llm_completion";
        public const string Chat = "chat";
    }

    public static class Outcomes
    {
        public const string Success = "success";
        public const string Timeout = "timeout";
        public const string ParseError = "parse_error";
        public const string Fallback = "fallback";
        public const string Error = "error";
        public const string Cancelled = "cancelled";
    }

    public static class Tags
    {
        public const string System = "gen_ai.system";
        public const string RequestModel = "gen_ai.request.model";
        public const string OperationName = "gen_ai.operation.name";
        public const string InputTokens = "gen_ai.usage.input_tokens";
        public const string OutputTokens = "gen_ai.usage.output_tokens";
        public const string TotalTokens = "gen_ai.usage.total_tokens";
        public const string ToolName = "gen_ai.tool.name";
        public const string Feature = "ai.feature";
        public const string LatencyMs = "ai.latency_ms";
        public const string RequestDurationMs = "ai.request_duration_ms";
        public const string Outcome = "ai.outcome";
        public const string JobId = "ai.job_id";
        public const string TodoExternalId = "todo.external_id";
        public const string InputChars = "ai.input_chars";
        public const string OutputChars = "ai.output_chars";
        public const string InputPreview = "ai.input_preview";
        public const string OutputPreview = "ai.output_preview";
        public const string ToolArgsPreview = "ai.tool.args_preview";
        public const string EmbeddingDimension = "ai.embedding.dimension";
        public const string Provider = "ai.provider";
    }
}
