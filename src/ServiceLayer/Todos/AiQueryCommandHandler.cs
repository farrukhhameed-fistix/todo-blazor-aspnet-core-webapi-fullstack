#nullable enable

using Fistix.TaskManager.AiLayer.Abstractions;
using Fistix.TaskManager.AiLayer.Implementations;
using Fistix.TaskManager.AiLayer.Models;
using Fistix.TaskManager.AiLayer.Shared;
using Fistix.TaskManager.Core.Abstractions.Repositories;
using Fistix.TaskManager.Core.Abstractions.Services;
using Fistix.TaskManager.Core.DomainModel.Aggregates;
using Fistix.TaskManager.Core.Exceptions;
using Fistix.TaskManager.Core.SecurityModel;
using Fistix.TaskManager.ViewModel.Commands.Todos;
using MediatR;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Fistix.TaskManager.ServiceLayer.Todos;

public class AiQueryCommandHandler : IRequestHandler<AiQueryCommand, AiQueryCommandResult>
{
    private readonly RAGPipeline _ragPipeline;
    private readonly SemanticSearchPipeline _semanticSearchPipeline;
    private readonly ITodoTaskRepository _todoTaskRepository;
    private readonly IAiConversationRepository _conversationRepository;
    private readonly ICurrentUserService _currentUserService;
    private readonly AiConfiguration _aiConfig;
    private readonly ILogger<AiQueryCommandHandler> _logger;

    public AiQueryCommandHandler(
        RAGPipeline ragPipeline,
        SemanticSearchPipeline semanticSearchPipeline,
        ITodoTaskRepository todoTaskRepository,
        IAiConversationRepository conversationRepository,
        ICurrentUserService currentUserService,
        AiConfiguration aiConfig,
        ILogger<AiQueryCommandHandler> logger)
    {
        _ragPipeline = ragPipeline;
        _semanticSearchPipeline = semanticSearchPipeline;
        _todoTaskRepository = todoTaskRepository;
        _conversationRepository = conversationRepository;
        _currentUserService = currentUserService;
        _aiConfig = aiConfig;
        _logger = logger;
    }

    public async Task<AiQueryCommandResult> Handle(AiQueryCommand command, CancellationToken cancellationToken)
    {
        if (!_aiConfig.Features.EnableRag)
        {
            throw new FeatureDisabledException("AI RAG");
        }

        var userId = TodoAccessGuard.RequireCurrentUserId(_currentUserService);
        var isAdmin = _currentUserService.HasAdminProfile;
        var intent = RagQueryIntent.Parse(command.Question);

        // Temporal phrases drive a due-date filter; otherwise semantic RAG on the topic query.
        var temporal = RagTemporalQuery.Detect(command.Question);
        if (temporal.IsTemporal)
        {
            return await HandleTemporalAsync(
                command.Question,
                intent,
                temporal,
                userId,
                isAdmin,
                cancellationToken);
        }

        return await HandleSemanticRagAsync(
            command.Question,
            intent,
            userId,
            isAdmin,
            cancellationToken);
    }

    private async Task<AiQueryCommandResult> HandleTemporalAsync(
        string question,
        RagQueryIntent intent,
        RagTemporalWindow window,
        Guid userId,
        bool isAdmin,
        CancellationToken cancellationToken)
    {
        var todos = isAdmin
            ? await _todoTaskRepository.GetAll(cancellationToken)
            : await _todoTaskRepository.GetByOwner(userId, cancellationToken);

        var excludeCompleted = intent.ExcludeCompleted || window.Kind == RagTemporalKind.Overdue;
        var retrievalLimit = GetRetrievalLimit();

        var matched = todos
            .Where(t => isAdmin || t.CreatedByUserId == userId)
            .Where(t => !excludeCompleted || !RagTemporalQuery.IsCompleted(t))
            .Where(t => RagTemporalQuery.Matches(t, window))
            .Where(t => intent.MatchesPriority(t.Priority))
            .OrderBy(t => t.DueDate)
            .ThenBy(t => t.Title)
            .Take(RagTemporalQuery.MaxTemporalResults)
            .ToList();

        foreach (var todo in matched)
        {
            if (!isAdmin)
            {
                TodoAccessGuard.EnsureCanAccess(todo, _currentUserService);
            }
        }

        // Empty window or plain list → deterministic (no LLM calendar guessing).
        if (matched.Count == 0 || RagTemporalQuery.IsPlainListQuestion(question))
        {
            var listSources = matched.Select(ToSourceDto).ToList();
            var answer = RagTemporalQuery.BuildDeterministicAnswer(matched, window);
            await SaveConversationAsync(
                userId, question, answer, listSources, "deterministic-temporal", cancellationToken);

            _logger.LogInformation(
                "RAG temporal list kind={Kind} matched={Count} for user {UserId}",
                window.Kind,
                matched.Count,
                userId);

            return new AiQueryCommandResult
            {
                Payload = new AiQueryResponseDto
                {
                    Answer = answer,
                    Sources = listSources,
                    Model = "deterministic-temporal"
                }
            };
        }

        // Non-list: date + structured filters, then hybrid (when enabled) within the window, then LLM.
        var ragTodos = RagQueryIntent.OrderForRag(matched).Take(retrievalLimit).ToList();
        var hybridEnabled = _aiConfig.Features.SemanticSearch?.HybridEnabled == true;

        if (hybridEnabled && matched.Count > 0)
        {
            var allowedIds = matched.Select(t => t.ExternalId).ToList();
            var search = await _semanticSearchPipeline.ExecuteAsync(new SemanticSearchPipelineRequest
            {
                Query = intent.EffectiveSearchQuery,
                Limit = retrievalLimit,
                OwnerExternalId = isAdmin ? null : userId,
                AllowedExternalIds = allowedIds
            }, cancellationToken);

            if (search.Hits.Count > 0)
            {
                var byId = matched.ToDictionary(t => t.ExternalId);
                var fromHits = search.Hits
                    .Select(h => byId.GetValueOrDefault(h.TodoExternalId))
                    .Where(t => t is not null)
                    .Cast<TodoTask>()
                    .ToList();
                if (fromHits.Count > 0)
                {
                    ragTodos = RagQueryIntent.OrderForRag(fromHits).ToList();
                }
            }
        }

        var ragSources = ragTodos.Select(ToRagSource).ToList();
        var retrievalDtos = ragTodos.Select(ToSourceDto).ToList();

        var rag = await _ragPipeline.ExecuteAsync(new RagPipelineRequest
        {
            Question = question,
            PreFilteredDateWindow = window.Label,
            PreFilteredPriority = intent.PriorityFilter,
            IsAdviceQuestion = intent.IsAdviceQuestion,
            SourceTodos = ragSources
        }, cancellationToken);

        var sourceDtos = SelectCitedSources(retrievalDtos, rag.Answer);
        await SaveConversationAsync(
            userId, question, rag.Answer, sourceDtos, rag.Model, cancellationToken);

        _logger.LogInformation(
            "RAG temporal+LLM kind={Kind} matched={Matched} sources={Sources} hybrid={Hybrid} search={Search} for user {UserId}",
            window.Kind,
            matched.Count,
            sourceDtos.Count,
            hybridEnabled,
            intent.EffectiveSearchQuery,
            userId);

        return new AiQueryCommandResult
        {
            Payload = new AiQueryResponseDto
            {
                Answer = rag.Answer,
                Sources = sourceDtos,
                Model = rag.Model
            }
        };
    }

    private async Task SaveConversationAsync(
        Guid userId,
        string question,
        string answer,
        List<AiQuerySourceDto> sources,
        string model,
        CancellationToken cancellationToken)
    {
        await _conversationRepository.AddAsync(new AiConversation
        {
            UserId = userId.ToString(),
            Query = question,
            Response = answer,
            ContextTodosJson = JsonSerializer.Serialize(sources.Select(s => s.ExternalId)),
            Model = model,
            CreatedAt = DateTime.UtcNow
        }, cancellationToken);
    }

    private async Task<AiQueryCommandResult> HandleSemanticRagAsync(
        string question,
        RagQueryIntent intent,
        Guid userId,
        bool isAdmin,
        CancellationToken cancellationToken)
    {
        var retrievalLimit = GetRetrievalLimit();
        if (intent.IsAdviceQuestion)
        {
            return await HandleAdviceAsync(
                question, intent, userId, isAdmin, retrievalLimit, cancellationToken);
        }

        // Over-fetch so priority/completed filters still leave enough hits.
        var searchLimit = Math.Min(
            Math.Max(retrievalLimit * 4, retrievalLimit),
            40);

        var search = await _semanticSearchPipeline.ExecuteAsync(new SemanticSearchPipelineRequest
        {
            Query = intent.EffectiveSearchQuery,
            Limit = searchLimit,
            OwnerExternalId = isAdmin ? null : userId
        }, cancellationToken);

        var sources = await LoadRagSourcesFromHitsAsync(
            search.Hits,
            intent,
            userId,
            isAdmin,
            retrievalLimit,
            excludeMeta: false,
            cancellationToken);

        sources = sources
            .OrderBy(s => RagQueryIntent.PriorityRank(s.Priority))
            .ThenBy(s => s.DueDate)
            .ThenBy(s => s.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var rag = await _ragPipeline.ExecuteAsync(new RagPipelineRequest
        {
            Question = question,
            PreFilteredPriority = intent.PriorityFilter,
            IsAdviceQuestion = false,
            SourceTodos = sources
        }, cancellationToken);

        var retrievalDtos = sources.Select(FromRagSource).ToList();
        var sourceDtos = SelectCitedSources(retrievalDtos, rag.Answer);

        await SaveConversationAsync(
            userId, question, rag.Answer, sourceDtos, rag.Model, cancellationToken);

        return new AiQueryCommandResult
        {
            Payload = new AiQueryResponseDto
            {
                Answer = rag.Answer,
                Sources = sourceDtos,
                Model = rag.Model
            }
        };
    }

    /// <summary>
    /// Advice Ask: multi-topic (or global open-todo) retrieval, fair quotas, meta filter, High→due sort, LLM narrates.
    /// Sources stay as the full retrieval shortlist (not citation-trimmed).
    /// </summary>
    private async Task<AiQueryCommandResult> HandleAdviceAsync(
        string question,
        RagQueryIntent intent,
        Guid userId,
        bool isAdmin,
        int retrievalLimit,
        CancellationToken cancellationToken)
    {
        var totalLimit = Math.Max(retrievalLimit, 8);
        IReadOnlyList<TodoTask> ordered;
        string topicsLog;

        if (intent.ShouldUseGlobalAdviceFallback())
        {
            ordered = await LoadGlobalAdviceTodosAsync(
                intent, userId, isAdmin, totalLimit, cancellationToken);
            topicsLog = "(global-open-todos)";
        }
        else
        {
            var topics = intent.TopicSearchQueries();
            topicsLog = string.Join("|", topics);
            var perTopicLimit = Math.Min(
                Math.Max(retrievalLimit * 3, retrievalLimit),
                40);
            var perTopicLists = new List<IReadOnlyList<TodoTask>>();

            foreach (var topic in topics)
            {
                var search = await _semanticSearchPipeline.ExecuteAsync(new SemanticSearchPipelineRequest
                {
                    Query = topic,
                    Limit = perTopicLimit,
                    OwnerExternalId = isAdmin ? null : userId
                }, cancellationToken);

                var topicTodos = new List<TodoTask>();
                var seenInTopic = new HashSet<Guid>();

                foreach (var hit in search.Hits)
                {
                    if (!seenInTopic.Add(hit.TodoExternalId))
                    {
                        continue;
                    }

                    try
                    {
                        var todo = await _todoTaskRepository.Get(hit.TodoExternalId, cancellationToken);
                        if (!isAdmin)
                        {
                            TodoAccessGuard.EnsureCanAccess(todo, _currentUserService);
                        }

                        if (!intent.MatchesTodo(todo) || RagQueryIntent.IsMetaPlanningTask(todo))
                        {
                            continue;
                        }

                        topicTodos.Add(todo);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Skipping advice source {TodoExternalId}", hit.TodoExternalId);
                    }
                }

                perTopicLists.Add(topicTodos);
            }

            ordered = RagQueryIntent.MergeTopicsFairly(perTopicLists, totalLimit);
        }

        var ragSources = ordered.Select(ToRagSource).ToList();
        var retrievalDtos = ordered.Select(ToSourceDto).ToList();

        var rag = await _ragPipeline.ExecuteAsync(new RagPipelineRequest
        {
            Question = question,
            PreFilteredPriority = intent.PriorityFilter,
            IsAdviceQuestion = true,
            SourceTodos = ragSources
        }, cancellationToken);

        // Advice: keep full shortlist in Sources so multi-domain coverage is visible.
        await SaveConversationAsync(
            userId, question, rag.Answer, retrievalDtos, rag.Model, cancellationToken);

        _logger.LogInformation(
            "RAG advice+LLM topics={Topics} count={Count} for user {UserId}",
            topicsLog,
            ordered.Count,
            userId);

        return new AiQueryCommandResult
        {
            Payload = new AiQueryResponseDto
            {
                Answer = rag.Answer,
                Sources = retrievalDtos,
                Model = rag.Model
            }
        };
    }

    /// <summary>
    /// Open-ended advice: rank the user's open todos (High → due) without a weak semantic query.
    /// </summary>
    private async Task<IReadOnlyList<TodoTask>> LoadGlobalAdviceTodosAsync(
        RagQueryIntent intent,
        Guid userId,
        bool isAdmin,
        int totalLimit,
        CancellationToken cancellationToken)
    {
        var todos = isAdmin
            ? await _todoTaskRepository.GetAll(cancellationToken)
            : await _todoTaskRepository.GetByOwner(userId, cancellationToken);

        var filtered = todos
            .Where(t => isAdmin || t.CreatedByUserId == userId)
            .Where(t => intent.MatchesTodo(t))
            .Where(t => !RagQueryIntent.IsMetaPlanningTask(t))
            .ToList();

        foreach (var todo in filtered)
        {
            if (!isAdmin)
            {
                TodoAccessGuard.EnsureCanAccess(todo, _currentUserService);
            }
        }

        return RagQueryIntent.OrderForRag(filtered).Take(totalLimit).ToList();
    }

    private async Task<List<RagSourceTodo>> LoadRagSourcesFromHitsAsync(
        IReadOnlyList<VectorSearchHit> hits,
        RagQueryIntent intent,
        Guid userId,
        bool isAdmin,
        int limit,
        bool excludeMeta,
        CancellationToken cancellationToken)
    {
        var sources = new List<RagSourceTodo>();
        foreach (var hit in hits)
        {
            if (sources.Count >= limit)
            {
                break;
            }

            try
            {
                var todo = await _todoTaskRepository.Get(hit.TodoExternalId, cancellationToken);
                if (!isAdmin)
                {
                    TodoAccessGuard.EnsureCanAccess(todo, _currentUserService);
                }

                if (!intent.MatchesTodo(todo))
                {
                    continue;
                }

                if (excludeMeta && RagQueryIntent.IsMetaPlanningTask(todo))
                {
                    continue;
                }

                sources.Add(ToRagSource(todo));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Skipping RAG source {TodoExternalId}", hit.TodoExternalId);
            }
        }

        return sources;
    }

    private int GetRetrievalLimit()
    {
        var configured = _aiConfig.Features.Rag?.RetrievalLimit ?? 5;
        return Math.Clamp(configured, 1, 50);
    }

    /// <summary>
    /// Prefer Sources that the answer actually cited; fall back to the retrieval set.
    /// </summary>
    public static List<AiQuerySourceDto> SelectCitedSources(
        IReadOnlyList<AiQuerySourceDto> retrieval,
        string answer)
    {
        if (retrieval.Count == 0)
        {
            return new List<AiQuerySourceDto>();
        }

        var cited = LlmOutputValidator.ExtractGuids(answer);
        if (cited.Count == 0)
        {
            return retrieval.ToList();
        }

        var byId = retrieval.ToDictionary(s => s.ExternalId);
        var selected = new List<AiQuerySourceDto>();
        foreach (var id in cited.Distinct())
        {
            if (byId.TryGetValue(id, out var dto))
            {
                selected.Add(dto);
            }
        }

        return selected.Count > 0 ? selected : retrieval.ToList();
    }

    private static AiQuerySourceDto FromRagSource(RagSourceTodo s) =>
        new()
        {
            ExternalId = s.ExternalId,
            Title = s.Title ?? string.Empty,
            DueDate = s.DueDate,
            Priority = s.Priority,
            Status = s.Status
        };

    private static RagSourceTodo ToRagSource(TodoTask todo) =>
        new()
        {
            ExternalId = todo.ExternalId,
            Title = todo.Title,
            Description = todo.Description,
            Priority = todo.Priority,
            Status = todo.Status,
            DueDate = todo.DueDate
        };

    private static AiQuerySourceDto ToSourceDto(TodoTask todo) =>
        new()
        {
            ExternalId = todo.ExternalId,
            Title = todo.Title ?? string.Empty,
            DueDate = todo.DueDate,
            Priority = todo.Priority,
            Status = todo.Status
        };
}
