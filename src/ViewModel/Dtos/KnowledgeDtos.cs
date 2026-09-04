#nullable enable

using System;
using System.Collections.Generic;

namespace Fistix.TaskManager.ViewModel.Dtos;

public class KnowledgeDocumentDto
{
    public Guid ExternalId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public string Status { get; set; } = string.Empty;
    public int ChunkCount { get; set; }
    public string? Error { get; set; }
    public Guid? IngestJobExternalId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class KnowledgeChunkDto
{
    public Guid ExternalId { get; set; }
    public Guid DocumentExternalId { get; set; }
    public int Ordinal { get; set; }
    public string Content { get; set; } = string.Empty;
    public string? Heading { get; set; }
}

public class KnowledgeIngestJobDto
{
    public Guid ExternalId { get; set; }
    public Guid DocumentExternalId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string CurrentStep { get; set; } = string.Empty;
    public string DocumentStatus { get; set; } = string.Empty;
    public int ChunkCount { get; set; }
    public int ChunksEmbedded { get; set; }
    public string? LastError { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? HeartbeatAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public class KnowledgeUploadResultDto
{
    public KnowledgeDocumentDto Document { get; set; } = new();
    public KnowledgeIngestJobDto Job { get; set; } = new();
}

public class KnowledgeRagTraceHitDto
{
    public Guid ChunkExternalId { get; set; }
    public Guid DocumentExternalId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public int Ordinal { get; set; }
    public double Similarity { get; set; }
    public bool FromVector { get; set; }
    public bool FromLexical { get; set; }
    public string SourceKind { get; set; } = "document";
}

public class KnowledgeRagRetrieveRoundDto
{
    public int Round { get; set; }
    public string SearchQuery { get; set; } = string.Empty;
    public int HitCount { get; set; }
    public int CandidateCount { get; set; }
}

public class KnowledgeRagTraceDto
{
    public string SanitizedQuestion { get; set; } = string.Empty;
    public string? RewrittenQuery { get; set; }
    public bool HybridEnabled { get; set; }
    public bool IncludeTodos { get; set; }
    public string EmbeddingModel { get; set; } = string.Empty;
    public string ChatModel { get; set; } = string.Empty;
    public int HitCount { get; set; }
    public int VectorCandidateCount { get; set; }
    public int LexicalCandidateCount { get; set; }
    public int RetrieveRounds { get; set; } = 1;
    public string Outcome { get; set; } = string.Empty;
    public List<KnowledgeRagTraceHitDto> Hits { get; set; } = [];
    public List<KnowledgeRagRetrieveRoundDto> Rounds { get; set; } = [];
}

public class KnowledgeQuerySourceDto
{
    public Guid ChunkExternalId { get; set; }
    public Guid DocumentExternalId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public int Ordinal { get; set; }
    public string? Heading { get; set; }
    public string Snippet { get; set; } = string.Empty;
    public double Similarity { get; set; }
    public string SourceKind { get; set; } = "document";
}

public class KnowledgeQueryResponseDto
{
    public string Answer { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public List<KnowledgeQuerySourceDto> Sources { get; set; } = [];
    public KnowledgeRagTraceDto Trace { get; set; } = new();
}
