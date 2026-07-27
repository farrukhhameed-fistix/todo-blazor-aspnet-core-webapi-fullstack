#nullable enable

using System;
using Fistix.TaskManager.ViewModel.Dtos;
using MediatR;
using System.Collections.Generic;

namespace Fistix.TaskManager.ViewModel.Queries.Todos;

public class GetAiBatchJobQuery : IRequest<GetAiBatchJobQueryResult>
{
    public Guid JobExternalId { get; set; }
}

public class GetAiBatchJobQueryResult
{
    public AiBatchJobDto Payload { get; set; } = new();
}

public class GetActiveAiBatchJobQuery : IRequest<GetActiveAiBatchJobQueryResult>
{
}

public class GetActiveAiBatchJobQueryResult
{
    public AiBatchJobDto? Payload { get; set; }
}

public class GetTodoImportBatchesQuery : IRequest<GetTodoImportBatchesQueryResult>
{
}

public class GetTodoImportBatchesQueryResult
{
    public List<TodoImportBatchDto> Payload { get; set; } = [];
}
