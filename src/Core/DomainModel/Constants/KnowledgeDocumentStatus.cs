namespace Fistix.TaskManager.Core.DomainModel.Constants;

public static class KnowledgeDocumentStatus
{
    public const string Pending = "Pending";
    public const string Parsing = "Parsing";
    public const string Chunking = "Chunking";
    public const string Embedding = "Embedding";
    public const string Ready = "Ready";
    public const string Failed = "Failed";
}
