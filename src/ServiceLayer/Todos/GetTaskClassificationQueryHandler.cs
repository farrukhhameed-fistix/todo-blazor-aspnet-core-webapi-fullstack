using Fistix.TaskManager.Core.Abstractions.Repositories;
using Fistix.TaskManager.Core.Abstractions.Services;
using Fistix.TaskManager.Core.DomainModel.Constants;
using Fistix.TaskManager.Core.SecurityModel;
using Fistix.TaskManager.ViewModel.Dtos;
using Fistix.TaskManager.ViewModel.Queries.Todos;
using MediatR;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Fistix.TaskManager.ServiceLayer.Todos;

public class GetTaskClassificationQueryHandler : IRequestHandler<GetTaskClassificationQuery, GetTaskClassificationQueryResult>
{
    private readonly ITodoTaskRepository _todoTaskRepository;
    private readonly ITodoAiMetadataRepository _todoAiMetadataRepository;
    private readonly ICurrentUserService _currentUserService;

    public GetTaskClassificationQueryHandler(
        ITodoTaskRepository todoTaskRepository,
        ITodoAiMetadataRepository todoAiMetadataRepository,
        ICurrentUserService currentUserService)
    {
        _todoTaskRepository = todoTaskRepository;
        _todoAiMetadataRepository = todoAiMetadataRepository;
        _currentUserService = currentUserService;
    }

    public async Task<GetTaskClassificationQueryResult> Handle(GetTaskClassificationQuery query, CancellationToken cancellationToken)
    {
        var todo = await _todoTaskRepository.Get(query.TodoExternalId, cancellationToken);
        TodoAccessGuard.EnsureCanAccess(todo, _currentUserService);

        var metadata = await _todoAiMetadataRepository.GetByTodoExternalIdAsync(query.TodoExternalId, cancellationToken);

        return new GetTaskClassificationQueryResult
        {
            Payload = metadata is null
                ? new TaskClassificationDto
                {
                    TodoExternalId = query.TodoExternalId,
                    Status = ClassificationStatus.None
                }
                : MapMetadata(query.TodoExternalId, metadata)
        };
    }

    private static TaskClassificationDto MapMetadata(Guid todoExternalId, Core.DomainModel.Aggregates.TodoAiMetadata metadata)
    {
        return new TaskClassificationDto
        {
            TodoExternalId = todoExternalId,
            SuggestedPriority = metadata.AiPriority,
            Confidence = metadata.ConfidenceScore,
            Reason = metadata.AiPriorityReason,
            Status = string.IsNullOrWhiteSpace(metadata.ClassificationStatus)
                ? ClassificationStatus.None
                : metadata.ClassificationStatus,
            Model = metadata.AiPriorityModel,
            FromCache = true,
            GeneratedAt = metadata.UpdatedAt ?? metadata.CreatedAt
        };
    }
}
