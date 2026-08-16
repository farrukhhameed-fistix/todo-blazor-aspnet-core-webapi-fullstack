#nullable enable

using Fistix.TaskManager.AiLayer.Abstractions;
using Fistix.TaskManager.AiLayer.Models;
using Fistix.TaskManager.AiLayer.Observability;
using Fistix.TaskManager.AiLayer.Shared;
using Fistix.TaskManager.AiLayer.Tools;
using Fistix.TaskManager.Core.Abstractions.Repositories;
using Fistix.TaskManager.Core.Abstractions.Services;
using Fistix.TaskManager.Core.DomainModel.Aggregates;
using Fistix.TaskManager.Core.SecurityModel;
using Fistix.TaskManager.ViewModel.Commands.Todos;
using MediatR;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Fistix.TaskManager.ServiceLayer.Todos;

/// <summary>
/// Executes approved tool calls via MediatR commands and repositories with TodoAccessGuard.
/// </summary>
public sealed class ToolExecutor : IToolExecutor
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IMediator _mediator;
    private readonly ITodoTaskRepository _todoTaskRepository;
    private readonly IToolExecutionLogRepository _toolExecutionLogRepository;
    private readonly ICurrentUserService _currentUserService;
    private readonly IAiTelemetry _telemetry;
    private readonly ILogger<ToolExecutor> _logger;

    public ToolExecutor(
        IMediator mediator,
        ITodoTaskRepository todoTaskRepository,
        IToolExecutionLogRepository toolExecutionLogRepository,
        ICurrentUserService currentUserService,
        ILogger<ToolExecutor> logger,
        IAiTelemetry? telemetry = null)
    {
        _mediator = mediator;
        _todoTaskRepository = todoTaskRepository;
        _toolExecutionLogRepository = toolExecutionLogRepository;
        _currentUserService = currentUserService;
        _logger = logger;
        _telemetry = telemetry ?? NullAiTelemetry.Instance;
    }

    public async Task<IReadOnlyList<ToolExecutionOutcome>> ExecuteAsync(
        IReadOnlyList<ProposedToolCall> calls,
        CancellationToken cancellationToken = default)
    {
        using var operation = _telemetry.StartOperation(AiTelemetryNames.Features.ExecuteTools);
        var userId = TodoAccessGuard.RequireCurrentUserId(_currentUserService);
        var outcomes = new List<ToolExecutionOutcome>();

        foreach (var call in calls)
        {
            var parametersJson = JsonSerializer.Serialize(call.Arguments, JsonOptions);
            var toolActivity = _telemetry.StartToolCall(call.ToolName, parametersJson);
            var sw = Stopwatch.StartNew();
            ToolExecutionOutcome outcome;

            try
            {
                if (!TodoToolDefinitions.IsAllowed(call.ToolName))
                {
                    throw new InvalidOperationException($"Tool '{call.ToolName}' is not allowed.");
                }

                var toolName = TodoToolDefinitions.NormalizeName(call.ToolName);
                var argValidation = ToolArgumentValidator.Validate(toolName, call.Arguments);
                if (!argValidation.IsValid)
                {
                    _telemetry.RecordQualityEvent(
                        AiTelemetryNames.Features.ExecuteTools,
                        AiTelemetryNames.QualityEvents.ToolArgRejected);
                    throw new InvalidOperationException(argValidation.Error ?? "Invalid tool arguments.");
                }

                outcome = toolName switch
                {
                    TodoToolDefinitions.CreateTodo => await ExecuteCreateAsync(call.Arguments, cancellationToken),
                    TodoToolDefinitions.UpdateTodo => await ExecuteUpdateAsync(call.Arguments, cancellationToken),
                    TodoToolDefinitions.MarkComplete => await ExecuteMarkCompleteAsync(call.Arguments, cancellationToken),
                    TodoToolDefinitions.SetPriority => await ExecuteSetPriorityAsync(call.Arguments, cancellationToken),
                    TodoToolDefinitions.SearchTodos => await ExecuteSearchAsync(call.Arguments, cancellationToken),
                    TodoToolDefinitions.GetStatistics => await ExecuteStatisticsAsync(cancellationToken),
                    TodoToolDefinitions.SetSemanticSearch => ExecuteSetSemanticSearch(call.Arguments),
                    TodoToolDefinitions.OpenTodo => ExecuteUiNoOp(TodoToolDefinitions.OpenTodo, "Open todo details in the UI."),
                    TodoToolDefinitions.CloseTodo => ExecuteUiNoOp(TodoToolDefinitions.CloseTodo, "Close todo dialog in the UI."),
                    TodoToolDefinitions.StartEdit => ExecuteUiNoOp(TodoToolDefinitions.StartEdit, "Open edit dialog in the UI."),
                    TodoToolDefinitions.CancelEdit => ExecuteUiNoOp(TodoToolDefinitions.CancelEdit, "Cancel edit dialog in the UI."),
                    TodoToolDefinitions.SaveEdit => ExecuteUiNoOp(TodoToolDefinitions.SaveEdit, "Save edit applied in the UI."),
                    TodoToolDefinitions.RegenerateSummary => ExecuteUiNoOp(TodoToolDefinitions.RegenerateSummary, "Regenerate summary in the UI."),
                    TodoToolDefinitions.RegeneratePriority => ExecuteUiNoOp(TodoToolDefinitions.RegeneratePriority, "Regenerate priority suggestion in the UI."),
                    TodoToolDefinitions.ApplySuggestedPriority => ExecuteUiNoOp(TodoToolDefinitions.ApplySuggestedPriority, "Apply suggested priority in the UI."),
                    _ => throw new InvalidOperationException($"Tool '{toolName}' is not implemented.")
                };

                sw.Stop();
                _telemetry.CompleteToolCall(toolActivity, sw.ElapsedMilliseconds, success: true);
                await LogAsync(userId, toolName, parametersJson, outcome, cancellationToken);
            }
            catch (Exception ex)
            {
                sw.Stop();
                _logger.LogWarning(ex, "Tool execution failed for {ToolName}", call.ToolName);
                outcome = new ToolExecutionOutcome
                {
                    ToolName = call.ToolName,
                    Success = false,
                    Message = ex.Message
                };
                _telemetry.CompleteToolCall(toolActivity, sw.ElapsedMilliseconds, success: false, error: ex.Message);
                await LogAsync(userId, call.ToolName, parametersJson, outcome, cancellationToken);
            }

            outcomes.Add(outcome);
        }

        operation.SetOutcome(outcomes.All(o => o.Success)
            ? AiTelemetryNames.Outcomes.Success
            : AiTelemetryNames.Outcomes.Error);
        return outcomes;
    }

    private async Task<ToolExecutionOutcome> ExecuteCreateAsync(
        Dictionary<string, JsonElement> args,
        CancellationToken cancellationToken)
    {
        var title = RequireString(args, "title");
        var description = GetString(args, "description") ?? title;
        var dueDate = GetDateTime(args, "dueDate") ?? DateTime.UtcNow.Date.AddDays(7);

        var createResult = await _mediator.Send(new CreateTodoTaskCommand
        {
            Title = title,
            Description = description,
            DueDate = dueDate
        }, cancellationToken);

        var priority = GetString(args, "priority");
        if (!string.IsNullOrWhiteSpace(priority) && createResult.Payload is not null)
        {
            await _mediator.Send(new UpdateTodoTaskCommand
            {
                ExternalId = createResult.Payload.ExternalId,
                Title = createResult.Payload.Title ?? title,
                Description = createResult.Payload.Description ?? description,
                DueDate = createResult.Payload.DueDate ?? dueDate,
                Priority = ClassificationGuardrails.ToTaskPriority(priority)
            }, cancellationToken);
        }

        var category = GetString(args, "category");
        if (!string.IsNullOrWhiteSpace(category) && createResult.Payload is not null)
        {
            if (category.Length > LlmInputLimits.CategoryMaxLength)
            {
                category = category[..LlmInputLimits.CategoryMaxLength];
            }

            var todo = await _todoTaskRepository.Get(createResult.Payload.ExternalId, cancellationToken);
            TodoAccessGuard.EnsureCanAccess(todo, _currentUserService);
            todo.Category = category;
            await _todoTaskRepository.Update(todo, cancellationToken);
        }

        return new ToolExecutionOutcome
        {
            ToolName = TodoToolDefinitions.CreateTodo,
            Success = true,
            Message = $"Created todo '{title}'.",
            ResultJson = JsonSerializer.Serialize(createResult.Payload, JsonOptions)
        };
    }

    private async Task<ToolExecutionOutcome> ExecuteUpdateAsync(
        Dictionary<string, JsonElement> args,
        CancellationToken cancellationToken)
    {
        var id = RequireGuid(args, "id");
        var todo = await _todoTaskRepository.Get(id, cancellationToken);
        TodoAccessGuard.EnsureCanAccess(todo, _currentUserService);

        var title = GetString(args, "title") ?? todo.Title;
        var description = GetString(args, "description") ?? todo.Description;
        var dueDate = GetDateTime(args, "dueDate") ?? todo.DueDate;
        var priority = GetString(args, "priority") ?? todo.Priority;
        var status = GetString(args, "status");

        var updateResult = await _mediator.Send(new UpdateTodoTaskCommand
        {
            ExternalId = id,
            Title = title,
            Description = description,
            DueDate = dueDate,
            Priority = ClassificationGuardrails.ToTaskPriority(priority)
        }, cancellationToken);

        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!ToolArgumentValidator.IsAllowedStatus(status))
            {
                throw new InvalidOperationException(
                    "Argument 'status' must be one of: Pending, InProgress, Completed.");
            }

            todo = await _todoTaskRepository.Get(id, cancellationToken);
            TodoAccessGuard.EnsureCanAccess(todo, _currentUserService);
            todo.Status = ToolArgumentValidator.NormalizeStatus(status);
            await _todoTaskRepository.Update(todo, cancellationToken);
        }

        return new ToolExecutionOutcome
        {
            ToolName = TodoToolDefinitions.UpdateTodo,
            Success = true,
            Message = $"Updated todo {id}.",
            ResultJson = JsonSerializer.Serialize(updateResult.Payload, JsonOptions)
        };
    }

    private async Task<ToolExecutionOutcome> ExecuteMarkCompleteAsync(
        Dictionary<string, JsonElement> args,
        CancellationToken cancellationToken)
    {
        var id = RequireGuid(args, "id");
        var todo = await _todoTaskRepository.Get(id, cancellationToken);
        TodoAccessGuard.EnsureCanAccess(todo, _currentUserService);
        todo.Status = "Completed";
        await _todoTaskRepository.Update(todo, cancellationToken);

        return new ToolExecutionOutcome
        {
            ToolName = TodoToolDefinitions.MarkComplete,
            Success = true,
            Message = $"Marked todo {id} as completed.",
            ResultJson = JsonSerializer.Serialize(new { id, status = todo.Status }, JsonOptions)
        };
    }

    private async Task<ToolExecutionOutcome> ExecuteSetPriorityAsync(
        Dictionary<string, JsonElement> args,
        CancellationToken cancellationToken)
    {
        var id = RequireGuid(args, "id");
        var priority = RequireString(args, "priority");
        var todo = await _todoTaskRepository.Get(id, cancellationToken);
        TodoAccessGuard.EnsureCanAccess(todo, _currentUserService);

        var updateResult = await _mediator.Send(new UpdateTodoTaskCommand
        {
            ExternalId = id,
            Title = todo.Title,
            Description = todo.Description,
            DueDate = todo.DueDate,
            Priority = ClassificationGuardrails.ToTaskPriority(priority)
        }, cancellationToken);

        return new ToolExecutionOutcome
        {
            ToolName = TodoToolDefinitions.SetPriority,
            Success = true,
            Message = $"Set priority of todo {id} to {ClassificationGuardrails.ToTaskPriority(priority)}.",
            ResultJson = JsonSerializer.Serialize(updateResult.Payload, JsonOptions)
        };
    }

    private async Task<ToolExecutionOutcome> ExecuteSearchAsync(
        Dictionary<string, JsonElement> args,
        CancellationToken cancellationToken)
    {
        var query = GetString(args, "query")?.Trim() ?? string.Empty;
        if (query.Length > LlmInputLimits.ToolSearchQueryMaxLength)
        {
            query = query[..LlmInputLimits.ToolSearchQueryMaxLength];
        }

        var statusFilter = GetString(args, "status");
        if (!string.IsNullOrWhiteSpace(statusFilter))
        {
            statusFilter = ToolArgumentValidator.NormalizeStatus(statusFilter);
        }

        var dueFrom = GetDateOnly(args, "dueFrom");
        var dueTo = GetDateOnly(args, "dueTo");

        var useSemantic = GetBool(args, "semantic") == true && !string.IsNullOrWhiteSpace(query);
        if (useSemantic)
        {
            var semanticResult = await _mediator.Send(new SemanticSearchTodosCommand
            {
                Query = query,
                Limit = 50
            }, cancellationToken);

            var hits = (semanticResult.Payload?.Results ?? [])
                .Where(h => MatchesStatus(h.Status, statusFilter))
                .ToList();

            // Semantic hits lack due dates — refine against owned todos when date filters present.
            if (dueFrom is not null || dueTo is not null)
            {
                var isAdmin = _currentUserService.HasAdminProfile;
                var userId = TodoAccessGuard.RequireCurrentUserId(_currentUserService);
                var todos = isAdmin
                    ? await _todoTaskRepository.GetAll(cancellationToken)
                    : await _todoTaskRepository.GetByOwner(userId, cancellationToken);
                var byId = todos.ToDictionary(t => t.ExternalId);
                hits = hits
                    .Where(h => byId.TryGetValue(h.TodoExternalId, out var todo) && MatchesDueDate(todo.DueDate, dueFrom, dueTo))
                    .ToList();
            }

            hits = hits.Take(20).ToList();

            return new ToolExecutionOutcome
            {
                ToolName = TodoToolDefinitions.SearchTodos,
                Success = true,
                Message = $"Found {hits.Count} matching todo(s) via semantic search.",
                ResultJson = JsonSerializer.Serialize(hits.Select(h => new
                {
                    h.TodoExternalId,
                    h.Title,
                    h.Priority,
                    h.Status,
                    h.Similarity,
                    Semantic = true
                }), JsonOptions)
            };
        }

        {
            var isAdmin = _currentUserService.HasAdminProfile;
            var userId = TodoAccessGuard.RequireCurrentUserId(_currentUserService);

            var todos = isAdmin
                ? await _todoTaskRepository.GetAll(cancellationToken)
                : await _todoTaskRepository.GetByOwner(userId, cancellationToken);

            var matches = todos
                .Where(t =>
                    (string.IsNullOrWhiteSpace(query)
                        || (t.Title?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
                        || (t.Description?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false))
                    && MatchesStatus(t.Status, statusFilter)
                    && MatchesDueDate(t.DueDate, dueFrom, dueTo))
                .Take(20)
                .Select(t => new
                {
                    t.ExternalId,
                    t.Title,
                    t.Priority,
                    t.Status,
                    t.DueDate,
                    Semantic = false
                })
                .ToList();

            return new ToolExecutionOutcome
            {
                ToolName = TodoToolDefinitions.SearchTodos,
                Success = true,
                Message = $"Found {matches.Count} matching todo(s).",
                ResultJson = JsonSerializer.Serialize(matches, JsonOptions)
            };
        }
    }

    private static bool MatchesStatus(string? status, string? statusFilter)
    {
        if (string.IsNullOrWhiteSpace(statusFilter))
        {
            return true;
        }

        return string.Equals(status, statusFilter, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesDueDate(DateTime? dueDate, DateTime? dueFrom, DateTime? dueTo)
    {
        if (dueFrom is null && dueTo is null)
        {
            return true;
        }

        if (dueDate is null)
        {
            return false;
        }

        var due = dueDate.Value.Date;
        if (dueFrom is not null && due < dueFrom.Value.Date)
        {
            return false;
        }

        if (dueTo is not null && due > dueTo.Value.Date)
        {
            return false;
        }

        return true;
    }

    private static DateTime? GetDateOnly(Dictionary<string, JsonElement> args, string name)
    {
        var raw = GetString(args, name);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (!DateTime.TryParse(raw, out var parsed))
        {
            throw new InvalidOperationException($"Argument '{name}' must be a valid date.");
        }

        return parsed.Date;
    }

    private static ToolExecutionOutcome ExecuteSetSemanticSearch(Dictionary<string, JsonElement> args)
    {
        var enabled = GetBool(args, "enabled") ?? false;
        return new ToolExecutionOutcome
        {
            ToolName = TodoToolDefinitions.SetSemanticSearch,
            Success = true,
            Message = enabled
                ? "Semantic search switch will be turned on in the UI."
                : "Semantic search switch will be turned off in the UI.",
            ResultJson = JsonSerializer.Serialize(new { enabled }, JsonOptions)
        };
    }

    private static ToolExecutionOutcome ExecuteUiNoOp(string toolName, string message) =>
        new()
        {
            ToolName = toolName,
            Success = true,
            Message = message,
            ResultJson = "{}"
        };

    private async Task<ToolExecutionOutcome> ExecuteStatisticsAsync(CancellationToken cancellationToken)
    {
        var isAdmin = _currentUserService.HasAdminProfile;
        var userId = TodoAccessGuard.RequireCurrentUserId(_currentUserService);

        var todos = isAdmin
            ? await _todoTaskRepository.GetAll(cancellationToken)
            : await _todoTaskRepository.GetByOwner(userId, cancellationToken);

        var stats = new
        {
            total = todos.Count,
            completed = todos.Count(t => string.Equals(t.Status, "Completed", StringComparison.OrdinalIgnoreCase)),
            pending = todos.Count(t => !string.Equals(t.Status, "Completed", StringComparison.OrdinalIgnoreCase)),
            highPriority = todos.Count(t => string.Equals(t.Priority, "High", StringComparison.OrdinalIgnoreCase)),
            mediumPriority = todos.Count(t => string.Equals(t.Priority, "Medium", StringComparison.OrdinalIgnoreCase)),
            lowPriority = todos.Count(t => string.Equals(t.Priority, "Low", StringComparison.OrdinalIgnoreCase)),
            overdue = todos.Count(t =>
                t.DueDate < DateTime.UtcNow &&
                !string.Equals(t.Status, "Completed", StringComparison.OrdinalIgnoreCase))
        };

        return new ToolExecutionOutcome
        {
            ToolName = TodoToolDefinitions.GetStatistics,
            Success = true,
            Message = "Computed todo statistics.",
            ResultJson = JsonSerializer.Serialize(stats, JsonOptions)
        };
    }

    private async Task LogAsync(
        Guid userId,
        string toolName,
        string parametersJson,
        ToolExecutionOutcome outcome,
        CancellationToken cancellationToken)
    {
        await _toolExecutionLogRepository.AddAsync(new ToolExecutionLog
        {
            UserId = userId.ToString(),
            ToolName = toolName,
            Parameters = parametersJson,
            Result = outcome.ResultJson,
            Success = outcome.Success,
            ErrorMessage = outcome.Success ? null : outcome.Message,
            ExecutedAt = DateTime.UtcNow
        }, cancellationToken);
    }

    private static string RequireString(Dictionary<string, JsonElement> args, string name)
    {
        var value = GetString(args, name);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Missing required argument '{name}'.");
        }

        return value;
    }

    private static string? GetString(Dictionary<string, JsonElement> args, string name)
    {
        if (!TryGetArg(args, name, out var element))
        {
            return null;
        }

        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => element.ToString()
        };
    }

    private static Guid RequireGuid(Dictionary<string, JsonElement> args, string name)
    {
        var raw = RequireString(args, name);
        if (!Guid.TryParse(raw, out var id))
        {
            throw new InvalidOperationException($"Argument '{name}' must be a valid GUID.");
        }

        return id;
    }

    private static DateTime? GetDateTime(Dictionary<string, JsonElement> args, string name)
    {
        var raw = GetString(args, name);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (!DateTime.TryParse(raw, out var parsed))
        {
            throw new InvalidOperationException($"Argument '{name}' must be a valid date/time.");
        }

        return parsed.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(parsed, DateTimeKind.Utc)
            : parsed.ToUniversalTime();
    }

    private static bool? GetBool(Dictionary<string, JsonElement> args, string name)
    {
        if (!TryGetArg(args, name, out var element))
        {
            return null;
        }

        return element.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(element.GetString(), out var parsed) => parsed,
            _ => null
        };
    }

    private static bool TryGetArg(Dictionary<string, JsonElement> args, string name, out JsonElement element)
    {
        foreach (var pair in args)
        {
            if (string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase))
            {
                element = pair.Value;
                return true;
            }
        }

        element = default;
        return false;
    }
}
