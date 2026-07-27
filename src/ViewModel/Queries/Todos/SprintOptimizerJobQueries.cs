#nullable enable

using System;
using Fistix.TaskManager.ViewModel.Dtos;
using MediatR;

namespace Fistix.TaskManager.ViewModel.Queries.Todos;

public class GetSprintOptimizerJobQuery : IRequest<GetSprintOptimizerJobQueryResult>
{
    public Guid JobExternalId { get; set; }
}

public class GetSprintOptimizerJobQueryResult
{
    public SprintOptimizerJobDto Payload { get; set; } = new();
}

public class GetActiveSprintOptimizerJobQuery : IRequest<GetActiveSprintOptimizerJobQueryResult>
{
}

public class GetActiveSprintOptimizerJobQueryResult
{
    public SprintOptimizerJobDto? Payload { get; set; }
}
