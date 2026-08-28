#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fistix.TaskManager.AiLayer.Abstractions;
using Fistix.TaskManager.AiLayer.Shared;
using Fistix.TaskManager.Core.Abstractions.Repositories;
using Fistix.TaskManager.Core.DomainModel.Aggregates;
using Fistix.TaskManager.Core.DomainModel.Constants;
using Fistix.TaskManager.ServiceLayer.Notifications;
using Microsoft.Extensions.Logging;

namespace Fistix.TaskManager.ServiceLayer.Knowledge;

public interface IKnowledgeIngestProcessor
{
    Task ProcessNextStepAsync(KnowledgeIngestJob job, CancellationToken cancellationToken);
}

public sealed class KnowledgeIngestProcessor : IKnowledgeIngestProcessor
{
    private readonly IKnowledgeDocumentRepository _documents;
    private readonly IKnowledgeChunkRepository _chunks;
    private readonly IKnowledgeChunkEmbeddingRepository _embeddings;
    private readonly IKnowledgeIngestJobRepository _jobs;
    private readonly IEmbeddingService _embeddingService;
    private readonly IKnowledgeIngestNotifier _notifier;
    private readonly AiConfiguration _aiConfig;
    private readonly ILogger<KnowledgeIngestProcessor> _logger;

    public KnowledgeIngestProcessor(
        IKnowledgeDocumentRepository documents,
        IKnowledgeChunkRepository chunks,
        IKnowledgeChunkEmbeddingRepository embeddings,
        IKnowledgeIngestJobRepository jobs,
        IEmbeddingService embeddingService,
        IKnowledgeIngestNotifier notifier,
        AiConfiguration aiConfig,
        ILogger<KnowledgeIngestProcessor> logger)
    {
        _documents = documents;
        _chunks = chunks;
        _embeddings = embeddings;
        _jobs = jobs;
        _embeddingService = embeddingService;
        _notifier = notifier;
        _aiConfig = aiConfig;
        _logger = logger;
    }

    public async Task ProcessNextStepAsync(KnowledgeIngestJob job, CancellationToken cancellationToken)
    {
        var document = await _documents.GetByIdAsync(job.DocumentId, cancellationToken);
        if (document is null)
        {
            job.Status = AiBatchJobStatus.Failed;
            job.LastError = "Document not found.";
            job.CompletedAt = DateTime.UtcNow;
            await _jobs.UpdateAsync(job, cancellationToken);
            return;
        }

        try
        {
            if (string.Equals(job.CurrentStep, KnowledgeIngestStepNames.Parse, StringComparison.OrdinalIgnoreCase))
            {
                await ParseAsync(document, job, cancellationToken);
                return;
            }

            if (string.Equals(job.CurrentStep, KnowledgeIngestStepNames.Chunk, StringComparison.OrdinalIgnoreCase))
            {
                await ChunkAsync(document, job, cancellationToken);
                return;
            }

            if (string.Equals(job.CurrentStep, KnowledgeIngestStepNames.Embed, StringComparison.OrdinalIgnoreCase))
            {
                await EmbedAsync(document, job, cancellationToken);
                return;
            }

            throw new InvalidOperationException($"Unknown ingest step '{job.CurrentStep}'.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Knowledge ingest failed for document {DocumentId} at {Step}",
                document.ExternalId, job.CurrentStep);
            document.Status = KnowledgeDocumentStatus.Failed;
            document.Error = TruncateError(ex.Message);
            job.Status = AiBatchJobStatus.Failed;
            job.LastError = document.Error;
            job.CompletedAt = DateTime.UtcNow;
            await _documents.UpdateAsync(document, cancellationToken);
            await _jobs.UpdateAsync(job, cancellationToken);
            await _notifier.NotifyAsync(KnowledgeMapper.ToJobDto(job, document), cancellationToken);
        }
    }

    private async Task ParseAsync(KnowledgeDocument document, KnowledgeIngestJob job, CancellationToken cancellationToken)
    {
        document.Status = KnowledgeDocumentStatus.Parsing;
        job.HeartbeatAt = DateTime.UtcNow;
        await _documents.UpdateAsync(document, cancellationToken);
        await _jobs.UpdateAsync(job, cancellationToken);
        await _notifier.NotifyAsync(KnowledgeMapper.ToJobDto(job, document), cancellationToken);

        var text = (document.ExtractedText ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException("Document is empty after parsing.");
        }

        document.ExtractedText = text;
        document.Error = null;
        job.CurrentStep = KnowledgeIngestStepNames.Chunk;
        job.HeartbeatAt = DateTime.UtcNow;
        await _documents.UpdateAsync(document, cancellationToken);
        await _jobs.UpdateAsync(job, cancellationToken);
        await _notifier.NotifyAsync(KnowledgeMapper.ToJobDto(job, document), cancellationToken);
    }

    private async Task ChunkAsync(KnowledgeDocument document, KnowledgeIngestJob job, CancellationToken cancellationToken)
    {
        document.Status = KnowledgeDocumentStatus.Chunking;
        job.HeartbeatAt = DateTime.UtcNow;
        await _documents.UpdateAsync(document, cancellationToken);
        await _jobs.UpdateAsync(job, cancellationToken);
        await _notifier.NotifyAsync(KnowledgeMapper.ToJobDto(job, document), cancellationToken);

        var cfg = _aiConfig.Features.KnowledgeRag ?? new KnowledgeRagConfiguration();
        var pieces = TextChunker.Split(document.ExtractedText, cfg.ChunkSize, cfg.ChunkOverlap);
        if (pieces.Count == 0)
        {
            throw new InvalidOperationException("Chunking produced no text chunks.");
        }

        var entities = new List<KnowledgeChunk>(pieces.Count);
        foreach (var piece in pieces)
        {
            var chunk = new KnowledgeChunk
            {
                DocumentId = document.Id,
                Ordinal = piece.Ordinal,
                Content = piece.Content,
                Heading = piece.Heading,
                CreatedAt = DateTime.UtcNow
            };
            chunk.GenerateNewExternalId();
            entities.Add(chunk);
        }

        await _chunks.ReplaceChunksAsync(document.Id, entities, cancellationToken);
        document.ChunkCount = entities.Count;
        job.CurrentStep = KnowledgeIngestStepNames.Embed;
        job.HeartbeatAt = DateTime.UtcNow;
        await _documents.UpdateAsync(document, cancellationToken);
        await _jobs.UpdateAsync(job, cancellationToken);
        await _notifier.NotifyAsync(KnowledgeMapper.ToJobDto(job, document), cancellationToken);
    }

    private async Task EmbedAsync(KnowledgeDocument document, KnowledgeIngestJob job, CancellationToken cancellationToken)
    {
        if (!_aiConfig.Features.EnableEmbeddings)
        {
            throw new InvalidOperationException("Embeddings are disabled.");
        }

        document.Status = KnowledgeDocumentStatus.Embedding;
        job.HeartbeatAt = DateTime.UtcNow;
        await _documents.UpdateAsync(document, cancellationToken);
        await _jobs.UpdateAsync(job, cancellationToken);
        await _notifier.NotifyAsync(KnowledgeMapper.ToJobDto(job, document), cancellationToken);

        var stored = await _chunks.GetByDocumentIdAsync(document.Id, cancellationToken);
        var embedded = 0;
        foreach (var chunk in stored)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var vector = await _embeddingService.GenerateEmbeddingAsync(
                chunk.Content,
                EmbeddingInputKind.Passage,
                cancellationToken);
            await _embeddings.UpsertAsync(chunk.Id, vector, _embeddingService.ModelName, cancellationToken);
            embedded++;
            job.ChunksEmbedded = embedded;
            job.HeartbeatAt = DateTime.UtcNow;
            await _jobs.UpdateAsync(job, cancellationToken);
            await _notifier.NotifyAsync(KnowledgeMapper.ToJobDto(job, document), cancellationToken);
        }

        document.Status = KnowledgeDocumentStatus.Ready;
        document.Error = null;
        job.Status = AiBatchJobStatus.Completed;
        job.ChunksEmbedded = embedded;
        job.CompletedAt = DateTime.UtcNow;
        job.HeartbeatAt = DateTime.UtcNow;
        await _documents.UpdateAsync(document, cancellationToken);
        await _jobs.UpdateAsync(job, cancellationToken);
        await _notifier.NotifyAsync(KnowledgeMapper.ToJobDto(job, document), cancellationToken);

        _logger.LogInformation(
            "Knowledge ingest completed for {DocumentId} ({ChunkCount} chunks)",
            document.ExternalId,
            embedded);
    }

    private static string TruncateError(string message) =>
        message.Length <= 2000 ? message : message[..2000];
}
