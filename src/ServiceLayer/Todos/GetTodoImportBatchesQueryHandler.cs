#nullable enable

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fistix.TaskManager.Core.Abstractions.Repositories;
using Fistix.TaskManager.Core.Abstractions.Services;
using Fistix.TaskManager.Core.SecurityModel;
using Fistix.TaskManager.ViewModel.Dtos;
using Fistix.TaskManager.ViewModel.Queries.Todos;
using MediatR;

namespace Fistix.TaskManager.ServiceLayer.Todos;

public sealed class GetTodoImportBatchesQueryHandler
    : IRequestHandler<GetTodoImportBatchesQuery, GetTodoImportBatchesQueryResult>
{
    private readonly ITodoTaskRepository _todoTaskRepository;
    private readonly ICurrentUserService _currentUserService;

    public GetTodoImportBatchesQueryHandler(
        ITodoTaskRepository todoTaskRepository,
        ICurrentUserService currentUserService)
    {
        _todoTaskRepository = todoTaskRepository;
        _currentUserService = currentUserService;
    }

    public async Task<GetTodoImportBatchesQueryResult> Handle(
        GetTodoImportBatchesQuery request,
        CancellationToken cancellationToken)
    {
        var ownerId = TodoAccessGuard.RequireCurrentUserId(_currentUserService);
        var batches = await _todoTaskRepository.GetImportBatchesByOwnerAsync(ownerId, cancellationToken);

        return new GetTodoImportBatchesQueryResult
        {
            Payload = batches.Select(Map).ToList()
        };
    }

    private static TodoImportBatchDto Map(TodoImportBatchSummary s)
    {
        var missingTotal = s.MissingEmbeddings + s.MissingClassify + s.MissingSummary;
        var aiStatus = missingTotal == 0
            ? "Complete"
            : missingTotal == (s.TodoCount * 3)
                ? "NotStarted"
                : "Partial";

        return new TodoImportBatchDto
        {
            ImportTag = s.ImportTag,
            TodoCount = s.TodoCount,
            OldestCreatedOn = s.OldestCreatedOn,
            NewestCreatedOn = s.NewestCreatedOn,
            MissingEmbeddings = s.MissingEmbeddings,
            MissingClassify = s.MissingClassify,
            MissingSummary = s.MissingSummary,
            AiStatus = aiStatus
        };
    }
}
