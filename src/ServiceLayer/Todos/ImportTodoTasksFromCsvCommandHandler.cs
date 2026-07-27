#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Fistix.TaskManager.Core.Abstractions.Repositories;
using Fistix.TaskManager.Core.Abstractions.Services;
using Fistix.TaskManager.Core.DomainModel.Aggregates;
using Fistix.TaskManager.Core.SecurityModel;
using Fistix.TaskManager.ViewModel.Commands.Todos;
using Fistix.TaskManager.ViewModel.Dtos;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Fistix.TaskManager.ServiceLayer.Todos;

public sealed class ImportTodoTasksFromCsvCommandHandler
    : IRequestHandler<ImportTodoTasksFromCsvCommand, ImportTodoTasksFromCsvCommandResult>
{
    public const int MaxImportRows = 200;

    private readonly ITodoTaskRepository _todoTaskRepository;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<ImportTodoTasksFromCsvCommandHandler> _logger;

    public ImportTodoTasksFromCsvCommandHandler(
        ITodoTaskRepository todoTaskRepository,
        ICurrentUserService currentUserService,
        ILogger<ImportTodoTasksFromCsvCommandHandler> logger)
    {
        _todoTaskRepository = todoTaskRepository;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task<ImportTodoTasksFromCsvCommandResult> Handle(
        ImportTodoTasksFromCsvCommand command,
        CancellationToken cancellationToken)
    {
        var ownerId = TodoAccessGuard.RequireCurrentUserId(_currentUserService);
        var importTag = string.IsNullOrWhiteSpace(command.ImportTag)
            ? $"csv-{DateTime.UtcNow:yyyyMMddHHmmss}"
            : command.ImportTag.Trim();

        var parsed = TodoCsvParser.Parse(command.CsvContent, MaxImportRows);
        var result = new TodoCsvImportResultDto
        {
            ImportTag = importTag,
            DryRun = command.DryRun,
            SkippedCount = parsed.Errors.Count,
            Errors = parsed.Errors
                .Select(e => new TodoCsvImportRowErrorDto { RowNumber = e.RowNumber, Message = e.Message })
                .ToList()
        };

        if (parsed.Rows.Count == 0)
        {
            return new ImportTodoTasksFromCsvCommandResult { Payload = result };
        }

        if (command.DryRun)
        {
            result.ImportedCount = parsed.Rows.Count;
            return new ImportTodoTasksFromCsvCommandResult { Payload = result };
        }

        if (command.ReplaceExistingTag)
        {
            var deleted = await _todoTaskRepository.DeleteByImportTagAsync(ownerId, importTag, cancellationToken);
            _logger.LogInformation(
                "Replaced import tag {ImportTag}: deleted {DeletedCount} existing todos",
                importTag,
                deleted);
        }

        var entities = parsed.Rows.Select(row =>
        {
            var todo = new TodoTask
            {
                Title = row.Title,
                Description = row.Description,
                DueDate = row.DueDate,
                Status = row.Status,
                Priority = row.Priority,
                Category = row.Category ?? string.Empty,
                CreatedOn = DateTime.UtcNow,
                CreatedByUserId = ownerId,
                ImportTag = importTag
            };
            todo.GenerateNewExternalId();
            return todo;
        }).ToList();

        await _todoTaskRepository.CreateManyAsync(entities, cancellationToken);

        result.ImportedCount = entities.Count;
        result.TodoExternalIds = entities.Select(t => t.ExternalId).ToList();

        _logger.LogInformation(
            "Imported {Count} todos with tag {ImportTag} (no AI metadata/queues)",
            entities.Count,
            importTag);

        return new ImportTodoTasksFromCsvCommandResult { Payload = result };
    }
}

public sealed class DeleteImportedTodosCommandHandler
    : IRequestHandler<DeleteImportedTodosCommand, DeleteImportedTodosCommandResult>
{
    private readonly ITodoTaskRepository _todoTaskRepository;
    private readonly ICurrentUserService _currentUserService;

    public DeleteImportedTodosCommandHandler(
        ITodoTaskRepository todoTaskRepository,
        ICurrentUserService currentUserService)
    {
        _todoTaskRepository = todoTaskRepository;
        _currentUserService = currentUserService;
    }

    public async Task<DeleteImportedTodosCommandResult> Handle(
        DeleteImportedTodosCommand command,
        CancellationToken cancellationToken)
    {
        var ownerId = TodoAccessGuard.RequireCurrentUserId(_currentUserService);
        var tag = command.ImportTag.Trim();
        var deleted = await _todoTaskRepository.DeleteByImportTagAsync(ownerId, tag, cancellationToken);

        return new DeleteImportedTodosCommandResult
        {
            Payload = new DeleteImportedTodosResultDto
            {
                ImportTag = tag,
                DeletedCount = deleted
            }
        };
    }
}
