#nullable enable

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fistix.TaskManager.AiLayer.Shared;
using Fistix.TaskManager.Core.Abstractions.Repositories;
using Fistix.TaskManager.Core.Abstractions.Services;
using Fistix.TaskManager.Core.Exceptions;
using Fistix.TaskManager.Core.SecurityModel;
using Fistix.TaskManager.ViewModel.Commands.Knowledge;
using Fistix.TaskManager.ViewModel.Dtos;
using Fistix.TaskManager.ViewModel.Queries.Knowledge;
using MediatR;

namespace Fistix.TaskManager.ServiceLayer.Knowledge;

public sealed class ListKnowledgeDocumentsQueryHandler
    : IRequestHandler<ListKnowledgeDocumentsQuery, ListKnowledgeDocumentsQueryResult>
{
    private readonly IKnowledgeDocumentRepository _documents;
    private readonly IKnowledgeIngestJobRepository _jobs;
    private readonly ICurrentUserService _currentUserService;
    private readonly AiConfiguration _aiConfig;

    public ListKnowledgeDocumentsQueryHandler(
        IKnowledgeDocumentRepository documents,
        IKnowledgeIngestJobRepository jobs,
        ICurrentUserService currentUserService,
        AiConfiguration aiConfig)
    {
        _documents = documents;
        _jobs = jobs;
        _currentUserService = currentUserService;
        _aiConfig = aiConfig;
    }

    public async Task<ListKnowledgeDocumentsQueryResult> Handle(
        ListKnowledgeDocumentsQuery request,
        CancellationToken cancellationToken)
    {
        RequireEnabled();
        var userId = TodoAccessGuard.RequireCurrentUserId(_currentUserService);
        var docs = await _documents.ListByOwnerAsync(userId, cancellationToken);
        var payload = new List<KnowledgeDocumentDto>(docs.Count);
        foreach (var document in docs)
        {
            var job = await _jobs.GetLatestByDocumentIdAsync(document.Id, cancellationToken);
            payload.Add(KnowledgeMapper.ToDocumentDto(document, job));
        }

        return new ListKnowledgeDocumentsQueryResult { Payload = payload };
    }

    private void RequireEnabled()
    {
        if (!_aiConfig.Features.EnableKnowledgeRag)
        {
            throw new FeatureDisabledException("Knowledge Lab RAG");
        }
    }
}

public sealed class GetKnowledgeDocumentQueryHandler
    : IRequestHandler<GetKnowledgeDocumentQuery, GetKnowledgeDocumentQueryResult>
{
    private readonly IKnowledgeDocumentRepository _documents;
    private readonly IKnowledgeIngestJobRepository _jobs;
    private readonly ICurrentUserService _currentUserService;
    private readonly AiConfiguration _aiConfig;

    public GetKnowledgeDocumentQueryHandler(
        IKnowledgeDocumentRepository documents,
        IKnowledgeIngestJobRepository jobs,
        ICurrentUserService currentUserService,
        AiConfiguration aiConfig)
    {
        _documents = documents;
        _jobs = jobs;
        _currentUserService = currentUserService;
        _aiConfig = aiConfig;
    }

    public async Task<GetKnowledgeDocumentQueryResult> Handle(
        GetKnowledgeDocumentQuery request,
        CancellationToken cancellationToken)
    {
        if (!_aiConfig.Features.EnableKnowledgeRag)
        {
            throw new FeatureDisabledException("Knowledge Lab RAG");
        }

        var document = await _documents.GetByExternalIdAsync(request.DocumentExternalId, cancellationToken)
                       ?? throw new NotFoundException("Document not found.");
        KnowledgeAccessGuard.EnsureOwner(document, _currentUserService);
        var job = await _jobs.GetLatestByDocumentIdAsync(document.Id, cancellationToken);
        return new GetKnowledgeDocumentQueryResult { Payload = KnowledgeMapper.ToDocumentDto(document, job) };
    }
}

public sealed class ListKnowledgeChunksQueryHandler
    : IRequestHandler<ListKnowledgeChunksQuery, ListKnowledgeChunksQueryResult>
{
    private readonly IKnowledgeDocumentRepository _documents;
    private readonly IKnowledgeChunkRepository _chunks;
    private readonly ICurrentUserService _currentUserService;
    private readonly AiConfiguration _aiConfig;

    public ListKnowledgeChunksQueryHandler(
        IKnowledgeDocumentRepository documents,
        IKnowledgeChunkRepository chunks,
        ICurrentUserService currentUserService,
        AiConfiguration aiConfig)
    {
        _documents = documents;
        _chunks = chunks;
        _currentUserService = currentUserService;
        _aiConfig = aiConfig;
    }

    public async Task<ListKnowledgeChunksQueryResult> Handle(
        ListKnowledgeChunksQuery request,
        CancellationToken cancellationToken)
    {
        if (!_aiConfig.Features.EnableKnowledgeRag)
        {
            throw new FeatureDisabledException("Knowledge Lab RAG");
        }

        var document = await _documents.GetByExternalIdAsync(request.DocumentExternalId, cancellationToken)
                       ?? throw new NotFoundException("Document not found.");
        KnowledgeAccessGuard.EnsureOwner(document, _currentUserService);
        var chunks = await _chunks.GetByDocumentIdAsync(document.Id, cancellationToken);
        return new ListKnowledgeChunksQueryResult
        {
            Payload = chunks.Select(c => KnowledgeMapper.ToChunkDto(c, document.ExternalId)).ToList()
        };
    }
}

public sealed class GetKnowledgeIngestJobQueryHandler
    : IRequestHandler<GetKnowledgeIngestJobQuery, GetKnowledgeIngestJobQueryResult>
{
    private readonly IKnowledgeIngestJobRepository _jobs;
    private readonly IKnowledgeDocumentRepository _documents;
    private readonly ICurrentUserService _currentUserService;
    private readonly AiConfiguration _aiConfig;

    public GetKnowledgeIngestJobQueryHandler(
        IKnowledgeIngestJobRepository jobs,
        IKnowledgeDocumentRepository documents,
        ICurrentUserService currentUserService,
        AiConfiguration aiConfig)
    {
        _jobs = jobs;
        _documents = documents;
        _currentUserService = currentUserService;
        _aiConfig = aiConfig;
    }

    public async Task<GetKnowledgeIngestJobQueryResult> Handle(
        GetKnowledgeIngestJobQuery request,
        CancellationToken cancellationToken)
    {
        if (!_aiConfig.Features.EnableKnowledgeRag)
        {
            throw new FeatureDisabledException("Knowledge Lab RAG");
        }

        var job = await _jobs.GetByExternalIdAsync(request.JobExternalId, cancellationToken)
                  ?? throw new NotFoundException("Ingest job not found.");
        KnowledgeAccessGuard.EnsureOwner(job, _currentUserService);
        var document = await _documents.GetByIdAsync(job.DocumentId, cancellationToken)
                       ?? throw new NotFoundException("Document not found.");
        return new GetKnowledgeIngestJobQueryResult { Payload = KnowledgeMapper.ToJobDto(job, document) };
    }
}

public sealed class DeleteKnowledgeDocumentCommandHandler
    : IRequestHandler<DeleteKnowledgeDocumentCommand, DeleteKnowledgeDocumentCommandResult>
{
    private readonly IKnowledgeDocumentRepository _documents;
    private readonly ICurrentUserService _currentUserService;
    private readonly AiConfiguration _aiConfig;

    public DeleteKnowledgeDocumentCommandHandler(
        IKnowledgeDocumentRepository documents,
        ICurrentUserService currentUserService,
        AiConfiguration aiConfig)
    {
        _documents = documents;
        _currentUserService = currentUserService;
        _aiConfig = aiConfig;
    }

    public async Task<DeleteKnowledgeDocumentCommandResult> Handle(
        DeleteKnowledgeDocumentCommand request,
        CancellationToken cancellationToken)
    {
        if (!_aiConfig.Features.EnableKnowledgeRag)
        {
            throw new FeatureDisabledException("Knowledge Lab RAG");
        }

        var document = await _documents.GetByExternalIdAsync(request.DocumentExternalId, cancellationToken)
                       ?? throw new NotFoundException("Document not found.");
        KnowledgeAccessGuard.EnsureOwner(document, _currentUserService);
        await _documents.DeleteAsync(document, cancellationToken);
        return new DeleteKnowledgeDocumentCommandResult { DocumentExternalId = request.DocumentExternalId };
    }
}
