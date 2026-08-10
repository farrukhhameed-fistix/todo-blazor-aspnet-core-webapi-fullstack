#nullable enable

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

        // Temporal phrases drive a due-date filter; otherwise semantic RAG on the raw question.
        var temporal = RagTemporalQuery.Detect(command.Question);
        if (temporal.IsTemporal)
        {
            return await HandleTemporalAsync(
                command.Question,
                temporal,
                userId,
                isAdmin,
                cancellationToken);
        }

        return await HandleSemanticRagAsync(
            command.Question,
            userId,
            isAdmin,
            cancellationToken);
    }

    private async Task<AiQueryCommandResult> HandleTemporalAsync(
        string question,
        RagTemporalWindow window,
        Guid userId,
        bool isAdmin,
        CancellationToken cancellationToken)
    {
        var todos = isAdmin
            ? await _todoTaskRepository.GetAll(cancellationToken)
            : await _todoTaskRepository.GetByOwner(userId, cancellationToken);

        var excludeCompleted = RagTemporalQuery.ShouldExcludeCompleted(question)
                               || window.Kind == RagTemporalKind.Overdue;

        var matched = todos
            .Where(t => isAdmin || t.CreatedByUserId == userId)
            .Where(t => !excludeCompleted || !RagTemporalQuery.IsCompleted(t))
            .Where(t => RagTemporalQuery.Matches(t, window))
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
            var sourceDtos = matched.Select(ToSourceDto).ToList();
            var answer = RagTemporalQuery.BuildDeterministicAnswer(matched, window);
            await SaveConversationAsync(
                userId, question, answer, sourceDtos, "deterministic-temporal", cancellationToken);

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
                    Sources = sourceDtos,
                    Model = "deterministic-temporal"
                }
            };
        }

        // Non-list: date filter first, then hybrid (when enabled) within the window, then LLM.
        var ragTodos = matched;
        var sourceDtosHybrid = matched.Select(ToSourceDto).ToList();
        var hybridEnabled = _aiConfig.Features.SemanticSearch?.HybridEnabled == true;

        if (hybridEnabled)
        {
            var allowedIds = matched.Select(t => t.ExternalId).ToList();
            var search = await _semanticSearchPipeline.ExecuteAsync(new SemanticSearchPipelineRequest
            {
                Query = question,
                Limit = 10,
                OwnerExternalId = isAdmin ? null : userId,
                AllowedExternalIds = allowedIds
            }, cancellationToken);

            if (search.Hits.Count > 0)
            {
                var byId = matched.ToDictionary(t => t.ExternalId);
                ragTodos = search.Hits
                    .Select(h => byId.GetValueOrDefault(h.TodoExternalId))
                    .Where(t => t is not null)
                    .Cast<TodoTask>()
                    .ToList();
                sourceDtosHybrid = ragTodos.Select(ToSourceDto).ToList();
            }
        }

        var ragSources = ragTodos.Select(t => new RagSourceTodo
        {
            ExternalId = t.ExternalId,
            Title = t.Title,
            Description = t.Description,
            Priority = t.Priority,
            Status = t.Status,
            DueDate = t.DueDate
        }).ToList();

        var rag = await _ragPipeline.ExecuteAsync(new RagPipelineRequest
        {
            Question = question,
            PreFilteredDateWindow = window.Label,
            SourceTodos = ragSources
        }, cancellationToken);

        await SaveConversationAsync(
            userId, question, rag.Answer, sourceDtosHybrid, rag.Model, cancellationToken);

        _logger.LogInformation(
            "RAG temporal+LLM kind={Kind} matched={Matched} sources={Sources} hybrid={Hybrid} for user {UserId}",
            window.Kind,
            matched.Count,
            sourceDtosHybrid.Count,
            hybridEnabled,
            userId);

        return new AiQueryCommandResult
        {
            Payload = new AiQueryResponseDto
            {
                Answer = rag.Answer,
                Sources = sourceDtosHybrid,
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
        Guid userId,
        bool isAdmin,
        CancellationToken cancellationToken)
    {
        var search = await _semanticSearchPipeline.ExecuteAsync(new SemanticSearchPipelineRequest
        {
            Query = question,
            Limit = 10,
            OwnerExternalId = isAdmin ? null : userId
        }, cancellationToken);

        var sources = new List<RagSourceTodo>();
        foreach (var hit in search.Hits)
        {
            try
            {
                var todo = await _todoTaskRepository.Get(hit.TodoExternalId, cancellationToken);
                if (!isAdmin)
                {
                    TodoAccessGuard.EnsureCanAccess(todo, _currentUserService);
                }

                sources.Add(new RagSourceTodo
                {
                    ExternalId = todo.ExternalId,
                    Title = todo.Title,
                    Description = todo.Description,
                    Priority = todo.Priority,
                    Status = todo.Status,
                    DueDate = todo.DueDate
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Skipping RAG source {TodoExternalId}", hit.TodoExternalId);
            }
        }

        var rag = await _ragPipeline.ExecuteAsync(new RagPipelineRequest
        {
            Question = question,
            SourceTodos = sources
        }, cancellationToken);

        var sourceDtos = sources.Select(s => new AiQuerySourceDto
        {
            ExternalId = s.ExternalId,
            Title = s.Title ?? string.Empty,
            DueDate = s.DueDate,
            Priority = s.Priority,
            Status = s.Status
        }).ToList();

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
