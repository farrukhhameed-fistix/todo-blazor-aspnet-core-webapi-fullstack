using AutoMapper;
using Fistix.TaskManager.AiLayer.Observability;
using Fistix.TaskManager.AiLayer.Shared;
using Fistix.TaskManager.Core.Abstractions.Repositories;
using Fistix.TaskManager.Core.Abstractions.Services;
using Fistix.TaskManager.Core.DomainModel.Constants;
using Fistix.TaskManager.Core.Exceptions;
using Fistix.TaskManager.Core.SecurityModel;
using Fistix.TaskManager.ViewModel.Commands.Todos;
using Fistix.TaskManager.ViewModel.Dtos;
using MediatR;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Fistix.TaskManager.ServiceLayer.Todos;

public class ApplyAiPriorityCommandHandler : IRequestHandler<ApplyAiPriorityCommand, ApplyAiPriorityCommandResult>
{
    private readonly ITodoTaskRepository _todoTaskRepository;
    private readonly ITodoAiMetadataRepository _todoAiMetadataRepository;
    private readonly ICurrentUserService _currentUserService;
    private readonly IMapper _mapper;
    private readonly AiConfiguration _aiConfig;
    private readonly IAiTelemetry _telemetry;

    public ApplyAiPriorityCommandHandler(
        ITodoTaskRepository todoTaskRepository,
        ITodoAiMetadataRepository todoAiMetadataRepository,
        ICurrentUserService currentUserService,
        IMapper mapper,
        AiConfiguration aiConfig,
        IAiTelemetry? telemetry = null)
    {
        _todoTaskRepository = todoTaskRepository;
        _todoAiMetadataRepository = todoAiMetadataRepository;
        _currentUserService = currentUserService;
        _mapper = mapper;
        _aiConfig = aiConfig;
        _telemetry = telemetry ?? NullAiTelemetry.Instance;
    }

    public async Task<ApplyAiPriorityCommandResult> Handle(ApplyAiPriorityCommand command, CancellationToken cancellationToken)
    {
        if (!_aiConfig.Features.EnableClassification)
        {
            throw new FeatureDisabledException("AI classification");
        }

        var todo = await _todoTaskRepository.Get(command.TodoExternalId, cancellationToken);
        TodoAccessGuard.EnsureCanAccess(todo, _currentUserService);

        var metadata = await _todoAiMetadataRepository.GetByTodoExternalIdAsync(command.TodoExternalId, cancellationToken);
        if (metadata is null
            || !string.Equals(metadata.ClassificationStatus, ClassificationStatus.Completed, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(metadata.AiPriority))
        {
            throw new InvalidOperationException("No completed AI priority suggestion is available for this task.");
        }

        todo.Priority = ClassificationGuardrails.ToTaskPriority(metadata.AiPriority);
        await _todoTaskRepository.Update(todo, cancellationToken);
        await _todoAiMetadataRepository.SetWasOverriddenAsync(todo.Id, wasOverridden: false, cancellationToken);
        _telemetry.RecordOverrideDecision(
            AiTelemetryNames.ConfidenceBands.FromConfidence(metadata.ConfidenceScore ?? 0f),
            wasOverridden: false);

        todo = await _todoTaskRepository.Get(command.TodoExternalId, cancellationToken);

        return new ApplyAiPriorityCommandResult
        {
            Payload = _mapper.Map<TodoTaskDto>(todo)
        };
    }
}
