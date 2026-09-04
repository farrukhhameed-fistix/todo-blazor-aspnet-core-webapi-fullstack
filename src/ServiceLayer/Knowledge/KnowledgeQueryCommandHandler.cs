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
using Fistix.TaskManager.Core.DomainModel.Aggregates;
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
    private readonly KnowledgeSemanticSearchPipeline _searchPipeline;
    private readonly KnowledgeQueryRewriter _rewriter;
    private readonly SemanticSearchPipeline _todoSearchPipeline;
    private readonly ITodoTaskRepository _todoTasks;
    private readonly IKnowledgeDocumentRepository _documents;
    private readonly ICurrentUserService _currentUserService;
    private readonly AiConfiguration _aiConfig;
    private readonly ILogger<KnowledgeQueryCommandHandler> _logger;

    public KnowledgeQueryCommandHandler(
        RAGPipeline ragPipeline,
        IEmbeddingService embeddingService,
        KnowledgeSemanticSearchPipeline searchPipeline,
        KnowledgeQueryRewriter rewriter,
        SemanticSearchPipeline todoSearchPipeline,
        ITodoTaskRepository todoTasks,
        IKnowledgeDocumentRepository documents,
        ICurrentUserService currentUserService,
        AiConfiguration aiConfig,
        ILogger<KnowledgeQueryCommandHandler> logger)
    {
        _ragPipeline = ragPipeline;
        _embeddingService = embeddingService;
        _searchPipeline = searchPipeline;
        _rewriter = rewriter;
        _todoSearchPipeline = todoSearchPipeline;
        _todoTasks = todoTasks;
        _documents = documents;
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
        var includeTodos = command.IncludeTodos;

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
            EmbeddingModel = _embeddingService.ModelName,
            HybridEnabled = cfg.HybridEnabled,
            IncludeTodos = includeTodos
        };

        if (string.IsNullOrWhiteSpace(sanitizedQuestion))
        {
            var empty = await _ragPipeline.ExecuteAsync(new RagPipelineRequest
            {
                Question = command.Question,
                CorpusKind = includeTodos ? RagCorpusKind.Unified : RagCorpusKind.Knowledge,
                Sources = []
            }, cancellationToken);

            trace.Outcome = AiTelemetryNames.Outcomes.ValidationFailed;
            trace.ChatModel = empty.Model;
            return Result(empty.Answer, empty.Model, [], trace);
        }

        var searchQuery = sanitizedQuestion;
        if (cfg.EnableQueryRewrite)
        {
            searchQuery = await _rewriter.RewriteAsync(sanitizedQuestion, missingHint: null, cancellationToken);
            if (!string.Equals(searchQuery, sanitizedQuestion, StringComparison.Ordinal))
            {
                trace.RewrittenQuery = searchQuery;
            }
        }

        var usedChunkIds = new HashSet<Guid>();
        var kept = new List<KnowledgeRetrievedChunk>();
        KnowledgeSemanticSearchResult? lastSearch = null;
        var round = 1;

        lastSearch = await RetrieveDocsAsync(
            searchQuery, userId, documentFilter, retrievalLimit, usedChunkIds, cancellationToken);
        kept.AddRange(lastSearch.Hits);
        foreach (var h in lastSearch.Hits)
        {
            usedChunkIds.Add(h.ChunkExternalId);
        }

        RecordRound(trace, round, searchQuery, lastSearch);

        var todoSources = includeTodos
            ? await RetrieveTodosAsync(searchQuery, userId, Math.Max(3, retrievalLimit / 2), cancellationToken)
            : [];

        var rag = await GenerateAsync(sanitizedQuestion, kept, todoSources, includeTodos, cancellationToken);
        ApplyOutcome(trace, rag);

        if (cfg.EnableAgenticRetrieve
            && string.Equals(trace.Outcome, AiTelemetryNames.Outcomes.InsufficientContext, StringComparison.Ordinal)
            && round < 2)
        {
            round = 2;
            var secondQuery = await _rewriter.RewriteAsync(
                sanitizedQuestion,
                missingHint: "Need additional document chunks; prior answer was insufficient.",
                cancellationToken);
            if (!string.IsNullOrWhiteSpace(secondQuery))
            {
                searchQuery = secondQuery;
                trace.RewrittenQuery = secondQuery;
            }

            var second = await RetrieveDocsAsync(
                searchQuery, userId, documentFilter, retrievalLimit, usedChunkIds, cancellationToken);
            lastSearch = second;
            foreach (var h in second.Hits)
            {
                if (usedChunkIds.Add(h.ChunkExternalId))
                {
                    kept.Add(h);
                }
            }

            RecordRound(trace, round, searchQuery, second);

            if (includeTodos && todoSources.Count == 0)
            {
                todoSources = await RetrieveTodosAsync(searchQuery, userId, Math.Max(3, retrievalLimit / 2), cancellationToken);
            }

            rag = await GenerateAsync(sanitizedQuestion, kept, todoSources, includeTodos, cancellationToken);
            ApplyOutcome(trace, rag);
        }

        trace.RetrieveRounds = round;
        trace.HitCount = kept.Count + todoSources.Count;
        trace.VectorCandidateCount = lastSearch?.VectorCandidateCount ?? 0;
        trace.LexicalCandidateCount = lastSearch?.LexicalCandidateCount ?? 0;
        trace.ChatModel = rag.Model;
        trace.Hits = kept.Select(ToTraceHit).Concat(todoSources.Select(ToTodoTraceHit)).ToList();

        var sourceDtos = kept.Select(ToSourceDto)
            .Concat(todoSources.Select(ToTodoSourceDto))
            .ToList();

        _logger.LogInformation(
            "Knowledge RAG answered with {HitCount} sources, rounds={Rounds}, hybrid={Hybrid}, todos={Todos}, outcome {Outcome}",
            sourceDtos.Count,
            round,
            cfg.HybridEnabled,
            includeTodos,
            trace.Outcome);

        return Result(rag.Answer, rag.Model, sourceDtos, trace);
    }

    private async Task<KnowledgeSemanticSearchResult> RetrieveDocsAsync(
        string query,
        Guid userId,
        Guid? documentFilter,
        int limit,
        HashSet<Guid> exclude,
        CancellationToken cancellationToken) =>
        await _searchPipeline.ExecuteAsync(new KnowledgeSemanticSearchRequest
        {
            Query = query,
            OwnerExternalId = userId,
            DocumentExternalId = documentFilter,
            Limit = limit,
            ExcludeChunkExternalIds = exclude.Count == 0 ? null : exclude
        }, cancellationToken);

    private async Task<List<TodoRagItem>> RetrieveTodosAsync(
        string query,
        Guid userId,
        int limit,
        CancellationToken cancellationToken)
    {
        if (!_aiConfig.Features.EnableRag && !_aiConfig.Features.EnableEmbeddings)
        {
            return [];
        }

        var search = await _todoSearchPipeline.ExecuteAsync(new SemanticSearchPipelineRequest
        {
            Query = query,
            Limit = limit,
            OwnerExternalId = userId
        }, cancellationToken);

        if (search.Hits.Count == 0)
        {
            return [];
        }

        var todos = await _todoTasks.GetByOwner(userId, cancellationToken);
        var byId = todos.ToDictionary(t => t.ExternalId);
        var items = new List<TodoRagItem>();
        foreach (var hit in search.Hits)
        {
            if (!byId.TryGetValue(hit.TodoExternalId, out var todo))
            {
                continue;
            }

            items.Add(new TodoRagItem
            {
                ExternalId = todo.ExternalId,
                Title = todo.Title ?? string.Empty,
                Description = todo.Description,
                Similarity = hit.Similarity
            });
        }

        return items;
    }

    private async Task<RagPipelineResult> GenerateAsync(
        string question,
        IReadOnlyList<KnowledgeRetrievedChunk> docs,
        IReadOnlyList<TodoRagItem> todos,
        bool includeTodos,
        CancellationToken cancellationToken)
    {
        var sources = docs.Select(d => new RagSource
        {
            ExternalId = d.ChunkExternalId,
            Title = string.IsNullOrWhiteSpace(d.Heading)
                ? $"{d.FileName} #{d.Ordinal + 1}"
                : $"{d.FileName} — {d.Heading}",
            Content = d.Content
        }).ToList();

        foreach (var todo in todos)
        {
            sources.Add(new RagSource
            {
                ExternalId = todo.ExternalId,
                Title = $"Todo: {todo.Title}",
                Content = string.IsNullOrWhiteSpace(todo.Description) ? todo.Title : $"{todo.Title}\n{todo.Description}"
            });
        }

        return await _ragPipeline.ExecuteAsync(new RagPipelineRequest
        {
            Question = question,
            CorpusKind = includeTodos ? RagCorpusKind.Unified : RagCorpusKind.Knowledge,
            Sources = sources
        }, cancellationToken);
    }

    private static void RecordRound(
        KnowledgeRagTraceDto trace,
        int round,
        string searchQuery,
        KnowledgeSemanticSearchResult search)
    {
        trace.Rounds.Add(new KnowledgeRagRetrieveRoundDto
        {
            Round = round,
            SearchQuery = searchQuery,
            HitCount = search.Hits.Count,
            CandidateCount = search.VectorCandidateCount + search.LexicalCandidateCount
        });
    }

    private static void ApplyOutcome(KnowledgeRagTraceDto trace, RagPipelineResult rag)
    {
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
    }

    private static KnowledgeQueryCommandResult Result(
        string answer,
        string model,
        List<KnowledgeQuerySourceDto> sources,
        KnowledgeRagTraceDto trace) =>
        new()
        {
            Payload = new KnowledgeQueryResponseDto
            {
                Answer = answer,
                Model = model,
                Sources = sources,
                Trace = trace
            }
        };

    private static KnowledgeRagTraceHitDto ToTraceHit(KnowledgeRetrievedChunk x) =>
        new()
        {
            ChunkExternalId = x.ChunkExternalId,
            DocumentExternalId = x.DocumentExternalId,
            FileName = x.FileName,
            Ordinal = x.Ordinal,
            Similarity = x.Similarity,
            FromVector = x.FromVector,
            FromLexical = x.FromLexical,
            SourceKind = "document"
        };

    private static KnowledgeRagTraceHitDto ToTodoTraceHit(TodoRagItem x) =>
        new()
        {
            ChunkExternalId = x.ExternalId,
            DocumentExternalId = Guid.Empty,
            FileName = x.Title,
            Ordinal = 0,
            Similarity = x.Similarity,
            FromVector = true,
            SourceKind = "todo"
        };

    private static KnowledgeQuerySourceDto ToSourceDto(KnowledgeRetrievedChunk x) =>
        new()
        {
            ChunkExternalId = x.ChunkExternalId,
            DocumentExternalId = x.DocumentExternalId,
            FileName = x.FileName,
            Ordinal = x.Ordinal,
            Heading = x.Heading,
            Snippet = TruncateSnippet(x.Content),
            Similarity = x.Similarity,
            SourceKind = "document"
        };

    private static KnowledgeQuerySourceDto ToTodoSourceDto(TodoRagItem x) =>
        new()
        {
            ChunkExternalId = x.ExternalId,
            DocumentExternalId = Guid.Empty,
            FileName = x.Title,
            Ordinal = 0,
            Heading = "Todo",
            Snippet = TruncateSnippet(x.Description ?? x.Title),
            Similarity = x.Similarity,
            SourceKind = "todo"
        };

    private static string TruncateSnippet(string content)
    {
        var trimmed = content.Trim().Replace("\n", " ", StringComparison.Ordinal);
        return trimmed.Length <= 240 ? trimmed : trimmed[..240] + "…";
    }

    private sealed class TodoRagItem
    {
        public Guid ExternalId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
        public double Similarity { get; set; }
    }
}
