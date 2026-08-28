namespace Fistix.TaskManager.AiLayer.Shared;

/// <summary>Prompt version labels recorded with predictions for eval attribution.</summary>
public static class AiPromptVersions
{
    public const string Classify = "classify.v1";
    public const string Summarize = "summarize.v1";
    public const string Rag = "rag.v1";
    public const string KnowledgeRag = "rag.knowledge.v1";
    public const string ProposeTools = "propose_tools.v1";
    public const string SprintOptimizer = "sprint_optimizer.v1";
}
