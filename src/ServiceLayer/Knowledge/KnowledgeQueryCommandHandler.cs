#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fistix.TaskManager.AiLayer.Abstractions;
using Fistix.TaskManager.AiLayer.Implementations;
using Fistix.TaskManager.AiLayer.Models;
using Fistix.TaskManager.AiLayer.Observability;
using Fistix.TaskManager.AiLayer.Shared;
using Fistix.TaskManager.Core.Abstractions.Repositories;
using Fistix.TaskManager.Core.Abstractions.Services;
using Fistix.TaskManager.Core.Exceptions;
using Fistix.TaskManager.Core.SecurityModel;
using Fistix.TaskManager.ViewModel.Commands.Knowledge;
using Fistix.TaskManager.ViewModel.Dtos;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Fistix.TaskManager.ServiceLayer.Knowledge;

public sealed class KnowledgeQueryCommandHandler
    : IRequestHandler<KnowledgeQueryCommand, KnowledgeQueryCommandResult>
{
    private readonly RAGPipeline _ragPipeline;
    private readonly IEmbeddingService _embeddingService;
    private readonly IKnowledgeDocumentRepository _documents;
    private readonly IKnowledgeChunkEmbeddingRepository _embeddings;
    private readonly ICurrentUserService _currentUserService;
    private readonly AiConfiguration _aiConfig;
    private readonly ILogger<KnowledgeQueryCommandHandler> _logger;

    public KnowledgeQueryCommandHandler(
        RAGPipeline ragPipeline,
        IEmbeddingService embeddingService,
        IKnowledgeDocumentRepository documents,
        IKnowledgeChunkEmbeddingRepository embeddings,
        ICurrentUserService currentUserService,
        AiConfiguration aiConfig,
        ILogger<KnowledgeQueryCommandHandler> logger)
    {
        _ragPipeline = ragPipeline;
        _embeddingService = embeddingService;
        _documents = documents;
        _embeddings = embeddings;
        _currentUserService = currentUserService;
        _aiConfig = aiConfig;
        _logger = logger;
    }

    public async Task<KnowledgeQueryCommandResult> Handle(
        KnowledgeQueryCommand command,
        CancellationToken cancellationToken)
    {
        if (!_aiConfig.Features.EnableKnowledgeRag)
        {
            throw new FeatureDisabledException("Knowledge Lab RAG");
        }

        if (!_aiConfig.Features.EnableEmbeddings)
        {
            throw new FeatureDisabledException("Embeddings");
        }

        var userId = TodoAccessGuard.RequireCurrentUserId(_currentUserService);
        var cfg = _aiConfig.Features.KnowledgeRag ?? new KnowledgeRagConfiguration();
        var retrievalLimit = Math.Clamp(cfg.RetrievalLimit <= 0 ? 5 : cfg.RetrievalLimit, 1, 25);
        var minSimilarity = cfg.MinSimilarity;

        Guid? documentFilter = null;
        if (command.DocumentExternalId.HasValue && command.DocumentExternalId.Value != Guid.Empty)
        {
            var document = await _documents.GetByExternalIdAsync(command.DocumentExternalId.Value, cancellationToken)
                           ?? throw new NotFoundException("Document not found.");
            KnowledgeAccessGuard.EnsureOwner(document, _currentUserService);
            documentFilter = document.ExternalId;
        }

        var sanitizedQuestion = PromptInputSanitizer.SanitizeAndTruncate(
            command.Question, LlmInputLimits.ToolSearchQueryMaxLength * 4);
        var trace = new KnowledgeRagTraceDto
        {
            SanitizedQuestion = sanitizedQuestion,
            EmbeddingModel = _embeddingService.ModelName
        };

        if (string.IsNullOrWhiteSpace(sanitizedQuestion))
        {
            var empty = await _ragPipeline.ExecuteAsync(new RagPipelineRequest
            {
                Question = command.Question,
                CorpusKind = RagCorpusKind.Knowledge,
                Sources = []
            }, cancellationToken);

            trace.Outcome = AiTelemetryNames.Outcomes.ValidationFailed;
            trace.ChatModel = empty.Model;
            return new KnowledgeQueryCommandResult
            {
                Payload = new KnowledgeQueryResponseDto
                {
                    Answer = empty.Answer,
                    Model = empty.Model,
                    Trace = trace
                }
            };
        }

        var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(
            sanitizedQuestion,
            EmbeddingInputKind.Query,
            cancellationToken);

        var hits = await _embeddings.SearchSimilarAsync(
            queryEmbedding,
            _embeddingService.ModelName,
            userId,
            retrievalLimit * 2,
            cancellationToken,
            documentFilter);

        var kept = hits
            .Select(h => new
            {
                Hit = h,
                Similarity = Math.Max(0, 1.0 - h.Distance)
            })
            .Where(x => x.Similarity >= minSimilarity)
            .Take(retrievalLimit)
            .ToList();

        trace.HitCount = kept.Count;
        trace.Hits = kept.Select(x => new KnowledgeRagTraceHitDto
        {
            ChunkExternalId = x.Hit.ChunkExternalId,
            DocumentExternalId = x.Hit.DocumentExternalId,
            FileName = x.Hit.FileName,
            Ordinal = x.Hit.Ordinal,
            Similarity = x.Similarity
        }).ToList();

        var sources = kept.Select(x => new RagSource
        {
            ExternalId = x.Hit.ChunkExternalId,
            Title = string.IsNullOrWhiteSpace(x.Hit.Heading)
                ? $"{x.Hit.FileName} #{x.Hit.Ordinal + 1}"
                : $"{x.Hit.FileName} — {x.Hit.Heading}",
            Content = x.Hit.Content
        }).ToList();

        var rag = await _ragPipeline.ExecuteAsync(new RagPipelineRequest
        {
            Question = sanitizedQuestion,
            CorpusKind = RagCorpusKind.Knowledge,
            Sources = sources
        }, cancellationToken);

        trace.ChatModel = rag.Model;
        if (string.Equals(rag.Answer, LlmOutputValidator.InsufficientKnowledgeContextMessage, StringComparison.Ordinal))
        {
            trace.Outcome = AiTelemetryNames.Outcomes.InsufficientContext;
        }
        else if (string.Equals(rag.Answer, LlmOutputValidator.UngroundedKnowledgeAnswerMessage, StringComparison.Ordinal))
        {
            trace.Outcome = AiTelemetryNames.Outcomes.ValidationFailed;
        }
        else
        {
            trace.Outcome = AiTelemetryNames.Outcomes.Success;
        }

        var sourceDtos = kept.Select(x => new KnowledgeQuerySourceDto
        {
            ChunkExternalId = x.Hit.ChunkExternalId,
            DocumentExternalId = x.Hit.DocumentExternalId,
            FileName = x.Hit.FileName,
            Ordinal = x.Hit.Ordinal,
            Heading = x.Hit.Heading,
            Snippet = TruncateSnippet(x.Hit.Content),
            Similarity = x.Similarity
        }).ToList();

        _logger.LogInformation(
            "Knowledge RAG answered with {HitCount} chunks, outcome {Outcome}",
            kept.Count,
            trace.Outcome);

        return new KnowledgeQueryCommandResult
        {
            Payload = new KnowledgeQueryResponseDto
            {
                Answer = rag.Answer,
                Model = rag.Model,
                Sources = sourceDtos,
                Trace = trace
            }
        };
    }

    private static string TruncateSnippet(string content)
    {
        var trimmed = content.Trim().Replace("\n", " ", StringComparison.Ordinal);
        return trimmed.Length <= 240 ? trimmed : trimmed[..240] + "…";
    }
}
