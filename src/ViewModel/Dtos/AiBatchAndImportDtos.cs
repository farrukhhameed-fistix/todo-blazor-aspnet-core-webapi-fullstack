#nullable enable

using System;
using System.Collections.Generic;

namespace Fistix.TaskManager.ViewModel.Dtos;

public class TodoCsvImportResultDto
{
    public string ImportTag { get; set; } = string.Empty;
    public int ImportedCount { get; set; }
    public int SkippedCount { get; set; }
    public bool DryRun { get; set; }
    public List<Guid> TodoExternalIds { get; set; } = [];
    public List<TodoCsvImportRowErrorDto> Errors { get; set; } = [];
}

public class TodoCsvImportRowErrorDto
{
    public int RowNumber { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class DeleteImportedTodosResultDto
{
    public string ImportTag { get; set; } = string.Empty;
    public int DeletedCount { get; set; }
}

public class TodoImportBatchDto
{
    public string ImportTag { get; set; } = string.Empty;
    public int TodoCount { get; set; }
    public DateTime OldestCreatedOn { get; set; }
    public DateTime NewestCreatedOn { get; set; }
    public int MissingEmbeddings { get; set; }
    public int MissingClassify { get; set; }
    public int MissingSummary { get; set; }

    /// <summary>NotStarted, Partial, or Complete based on missing AI counts.</summary>
    public string AiStatus { get; set; } = "NotStarted";
}

public class AiBatchJobDto
{
    public Guid ExternalId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string CurrentStep { get; set; } = string.Empty;
    public IReadOnlyList<string> Steps { get; set; } = [];
    public int Cursor { get; set; }
    public int Total { get; set; }
    public int Completed { get; set; }
    public int Failed { get; set; }
    public int Skipped { get; set; }
    public int BatchSize { get; set; }
    public int DelayMsBetweenItems { get; set; }
    public bool OnlyMissing { get; set; }
    public string? ImportTag { get; set; }
    public string? LastError { get; set; }
    public Guid? LastTodoExternalId { get; set; }
    public double PercentComplete { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? HeartbeatAt { get; set; }
    public DateTime? PausedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
