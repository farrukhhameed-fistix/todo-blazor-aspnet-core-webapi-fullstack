#nullable enable

using Fistix.TaskManager.Core.DomainModel.Aggregates;
using Fistix.TaskManager.ViewModel.Dtos;

namespace Fistix.TaskManager.ServiceLayer.Knowledge;

public static class KnowledgeMapper
{
    public static KnowledgeDocumentDto ToDocumentDto(KnowledgeDocument document, KnowledgeIngestJob? job = null) =>
        new()
        {
            ExternalId = document.ExternalId,
            FileName = document.FileName,
            ContentType = document.ContentType,
            FileSizeBytes = document.FileSizeBytes,
            Status = document.Status,
            ChunkCount = document.ChunkCount,
            Error = document.Error,
            IngestJobExternalId = job?.ExternalId,
            CreatedAt = document.CreatedAt,
            UpdatedAt = document.UpdatedAt
        };

    public static KnowledgeChunkDto ToChunkDto(KnowledgeChunk chunk, System.Guid documentExternalId) =>
        new()
        {
            ExternalId = chunk.ExternalId,
            DocumentExternalId = documentExternalId,
            Ordinal = chunk.Ordinal,
            Content = chunk.Content,
            Heading = chunk.Heading
        };

    public static KnowledgeIngestJobDto ToJobDto(KnowledgeIngestJob job, KnowledgeDocument document) =>
        new()
        {
            ExternalId = job.ExternalId,
            DocumentExternalId = document.ExternalId,
            FileName = document.FileName,
            Status = job.Status,
            CurrentStep = job.CurrentStep,
            DocumentStatus = document.Status,
            ChunkCount = document.ChunkCount,
            ChunksEmbedded = job.ChunksEmbedded,
            LastError = job.LastError,
            CreatedAt = job.CreatedAt,
            UpdatedAt = job.UpdatedAt,
            HeartbeatAt = job.HeartbeatAt,
            CompletedAt = job.CompletedAt
        };
}
