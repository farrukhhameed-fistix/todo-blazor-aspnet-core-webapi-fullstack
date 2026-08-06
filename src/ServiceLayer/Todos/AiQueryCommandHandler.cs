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
        var context = command.Context.Trim().ToLowerInvariant();

        var search = await _semanticSearchPipeline.ExecuteAsync(new SemanticSearchPipelineRequest
        {
            Query = EnrichQuery(command.Question, context),
            Limit = 10,
            OwnerExternalId = isAdmin ? null : userId
        }, cancellationToken);

        var sources = new List<RagSourceTodo>();
        var sourceIds = new List<Guid>();
        foreach (var hit in search.Hits)
        {
            try
            {
                var todo = await _todoTaskRepository.Get(hit.TodoExternalId, cancellationToken);
                if (!isAdmin)
                {
                    TodoAccessGuard.EnsureCanAccess(todo, _currentUserService);
                }

                if (!MatchesContextFilter(todo, context))
                {
                    continue;
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
                sourceIds.Add(todo.ExternalId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Skipping RAG source {TodoExternalId}", hit.TodoExternalId);
            }
        }

        // Empty retrieval: still go through RAGPipeline so insufficient-context is recorded once.
        var rag = await _ragPipeline.ExecuteAsync(new RagPipelineRequest
        {
            Question = command.Question,
            Context = context,
            SourceTodos = sources
        }, cancellationToken);

        await _conversationRepository.AddAsync(new AiConversation
        {
            UserId = userId.ToString(),
            Query = command.Question,
            Response = rag.Answer,
            Context = context,
            ContextTodosJson = JsonSerializer.Serialize(sourceIds),
            Model = rag.Model,
            CreatedAt = DateTime.UtcNow
        }, cancellationToken);

        return new AiQueryCommandResult
        {
            Payload = new AiQueryResponseDto
            {
                Answer = rag.Answer,
                Sources = sourceIds,
                Model = rag.Model,
                Context = context
            }
        };
    }

    private static string EnrichQuery(string question, string context) => context switch
    {
        "week" => $"{question} tasks due this week high priority",
        "project" => $"{question} project workstream tasks",
        _ => question
    };

    private static bool MatchesContextFilter(TodoTask todo, string context)
    {
        if (context == "week")
        {
            var start = DateTime.UtcNow.Date;
            var end = start.AddDays(7);
            return todo.DueDate >= start && todo.DueDate < end;
        }

        return true;
    }
}
