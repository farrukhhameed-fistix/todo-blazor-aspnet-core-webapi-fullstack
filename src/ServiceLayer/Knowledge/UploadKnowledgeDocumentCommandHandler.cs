#nullable enable

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fistix.TaskManager.AiLayer.Shared;
using Fistix.TaskManager.Core.Abstractions.Repositories;
using Fistix.TaskManager.Core.Abstractions.Services;
using Fistix.TaskManager.Core.DomainModel.Aggregates;
using Fistix.TaskManager.Core.DomainModel.Constants;
using Fistix.TaskManager.Core.Exceptions;
using Fistix.TaskManager.Core.SecurityModel;
using Fistix.TaskManager.ViewModel.Commands.Knowledge;
using Fistix.TaskManager.ViewModel.Dtos;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Fistix.TaskManager.ServiceLayer.Knowledge;

public sealed class UploadKnowledgeDocumentCommandHandler
    : IRequestHandler<UploadKnowledgeDocumentCommand, UploadKnowledgeDocumentCommandResult>
{
    private static readonly string[] AllowedExtensions = [".txt", ".md"];

    private readonly IKnowledgeDocumentRepository _documents;
    private readonly IKnowledgeIngestJobRepository _jobs;
    private readonly ICurrentUserService _currentUserService;
    private readonly AiConfiguration _aiConfig;
    private readonly ILogger<UploadKnowledgeDocumentCommandHandler> _logger;

    public UploadKnowledgeDocumentCommandHandler(
        IKnowledgeDocumentRepository documents,
        IKnowledgeIngestJobRepository jobs,
        ICurrentUserService currentUserService,
        AiConfiguration aiConfig,
        ILogger<UploadKnowledgeDocumentCommandHandler> logger)
    {
        _documents = documents;
        _jobs = jobs;
        _currentUserService = currentUserService;
        _aiConfig = aiConfig;
        _logger = logger;
    }

    public async Task<UploadKnowledgeDocumentCommandResult> Handle(
        UploadKnowledgeDocumentCommand command,
        CancellationToken cancellationToken)
    {
        RequireKnowledgeRagEnabled();
        var userId = TodoAccessGuard.RequireCurrentUserId(_currentUserService);
        var cfg = _aiConfig.Features.KnowledgeRag ?? new KnowledgeRagConfiguration();

        var fileName = Path.GetFileName(command.FileName)?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new InvalidOperationException("File name is required.");
        }

        var extension = Path.GetExtension(fileName);
        if (!AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Only .txt and .md files are supported.");
        }

        var content = command.Content ?? string.Empty;
        var size = System.Text.Encoding.UTF8.GetByteCount(content);
        if (size > cfg.MaxUploadBytes)
        {
            throw new InvalidOperationException($"File exceeds the {cfg.MaxUploadBytes} byte upload limit.");
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException("File is empty.");
        }

        var document = new KnowledgeDocument
        {
            CreatedByUserId = userId,
            FileName = fileName,
            ContentType = string.IsNullOrWhiteSpace(command.ContentType)
                ? InferContentType(extension)
                : command.ContentType.Trim(),
            FileSizeBytes = size,
            Status = KnowledgeDocumentStatus.Pending,
            ExtractedText = content,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        document.GenerateNewExternalId();
        await _documents.CreateAsync(document, cancellationToken);

        var job = new KnowledgeIngestJob
        {
            DocumentId = document.Id,
            CreatedByUserId = userId,
            CurrentStep = KnowledgeIngestStepNames.Parse,
            Status = AiBatchJobStatus.Pending,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        job.GenerateNewExternalId();
        await _jobs.CreateAsync(job, cancellationToken);

        _logger.LogInformation(
            "Queued knowledge ingest for {DocumentId} ({FileName})",
            document.ExternalId,
            document.FileName);

        return new UploadKnowledgeDocumentCommandResult
        {
            Payload = new KnowledgeUploadResultDto
            {
                Document = KnowledgeMapper.ToDocumentDto(document, job),
                Job = KnowledgeMapper.ToJobDto(job, document)
            }
        };
    }

    private void RequireKnowledgeRagEnabled()
    {
        if (!_aiConfig.Features.EnableKnowledgeRag)
        {
            throw new FeatureDisabledException("Knowledge Lab RAG");
        }

        if (!_aiConfig.Features.EnableEmbeddings)
        {
            throw new FeatureDisabledException("Embeddings");
        }
    }

    private static string InferContentType(string extension) =>
        string.Equals(extension, ".md", StringComparison.OrdinalIgnoreCase)
            ? "text/markdown"
            : "text/plain";
}
