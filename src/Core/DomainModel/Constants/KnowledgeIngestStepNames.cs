namespace Fistix.TaskManager.Core.DomainModel.Constants;

public static class KnowledgeIngestStepNames
{
    public const string Parse = "Parse";
    public const string Chunk = "Chunk";
    public const string Embed = "Embed";

    public static readonly string[] OrderedSteps = [Parse, Chunk, Embed];
}
