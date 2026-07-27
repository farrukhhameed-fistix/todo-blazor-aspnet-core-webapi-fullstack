#nullable enable

using System;
using System.Collections.Generic;
using Fistix.TaskManager.ViewModel.Dtos;
using MediatR;

namespace Fistix.TaskManager.ViewModel.Commands.Todos;

public class ImportTodoTasksFromCsvCommand : IRequest<ImportTodoTasksFromCsvCommandResult>
{
    public string CsvContent { get; set; } = string.Empty;
    public string? ImportTag { get; set; }
    public bool DryRun { get; set; }
    public bool ReplaceExistingTag { get; set; }
}

public class ImportTodoTasksFromCsvCommandResult
{
    public TodoCsvImportResultDto Payload { get; set; } = new();
}

public class DeleteImportedTodosCommand : IRequest<DeleteImportedTodosCommandResult>
{
    public string ImportTag { get; set; } = string.Empty;
}

public class DeleteImportedTodosCommandResult
{
    public DeleteImportedTodosResultDto Payload { get; set; } = new();
}

public class StartAiBatchJobCommand : IRequest<StartAiBatchJobCommandResult>
{
    public string? ImportTag { get; set; }
    public List<Guid>? TodoExternalIds { get; set; }
    public List<string>? Steps { get; set; }
    public int BatchSize { get; set; } = 5;
    public int DelayMsBetweenItems { get; set; } = 500;
    public bool OnlyMissing { get; set; } = true;
}

public class StartAiBatchJobCommandResult
{
    public AiBatchJobDto Payload { get; set; } = new();
}

public class PauseAiBatchJobCommand : IRequest<AiBatchJobCommandResult>
{
    public Guid JobExternalId { get; set; }
}

public class ContinueAiBatchJobCommand : IRequest<AiBatchJobCommandResult>
{
    public Guid JobExternalId { get; set; }
}

public class CancelAiBatchJobCommand : IRequest<AiBatchJobCommandResult>
{
    public Guid JobExternalId { get; set; }
}

public class AiBatchJobCommandResult
{
    public AiBatchJobDto Payload { get; set; } = new();
}
