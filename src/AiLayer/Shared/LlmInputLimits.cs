namespace Fistix.TaskManager.AiLayer.Shared;

/// <summary>
/// Input/output limits for LLM prompts and validated responses.
/// Title/description/summary mirror ViewModel TodoFieldLimits values.
/// </summary>
public static class LlmInputLimits
{
    public const int TitleMaxLength = 200;
    public const int DescriptionMaxLength = 4000;
    public const int SummaryMaxLength = 500;

    public const int ReasonMaxLength = 300;
    public const int RagAnswerMaxLength = 4000;
    public const int RagContextDescriptionMaxLength = 1000;
    public const int RagTotalContextMaxLength = 12000;
    public const int AgentTextMaxLength = 4000;
    public const int ToolSearchQueryMaxLength = 500;
    public const int ExplanationMaxLength = 1000;
    public const int SemanticSearchQueryMaxLength = 500;
    public const int CategoryMaxLength = 100;
}
