#nullable enable

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fistix.TaskManager.AiLayer.Observability;
using Fistix.TaskManager.AiLayer.Shared;
using Fistix.TaskManager.Core.Abstractions.Repositories;
using Fistix.TaskManager.Core.Abstractions.Services;
using Fistix.TaskManager.Core.DomainModel.Constants;
using Fistix.TaskManager.Core.Exceptions;
using Fistix.TaskManager.Core.SecurityModel;
using Fistix.TaskManager.ServiceLayer.Notifications;
using Fistix.TaskManager.ViewModel.Commands.Todos;
using Fistix.TaskManager.ViewModel.Dtos;
using MediatR;

namespace Fistix.TaskManager.ServiceLayer.Todos;

public sealed class ApproveSprintOptimizerProposalCommandHandler
    : IRequestHandler<ApproveSprintOptimizerProposalCommand, ApproveSprintOptimizerProposalCommandResult>
{
    private readonly ISprintOptimizerJobRepository _jobRepository;
    private readonly ICurrentUserService _currentUserService;
    private readonly ISprintOptimizerNotifier _notifier;
    private readonly SprintOptimizerPersistService _persistService;
    private readonly IAiTelemetry _telemetry;
    private readonly AiConfiguration _aiConfig;

    public ApproveSprintOptimizerProposalCommandHandler(
        ISprintOptimizerJobRepository jobRepository,
        ICurrentUserService currentUserService,
        ISprintOptimizerNotifier notifier,
        SprintOptimizerPersistService persistService,
        IAiTelemetry? telemetry = null,
        AiConfiguration? aiConfig = null)
    {
        _jobRepository = jobRepository;
        _currentUserService = currentUserService;
        _notifier = notifier;
        _persistService = persistService;
        _telemetry = telemetry ?? NullAiTelemetry.Instance;
        _aiConfig = aiConfig ?? new AiConfiguration();
    }

    public async Task<ApproveSprintOptimizerProposalCommandResult> Handle(
        ApproveSprintOptimizerProposalCommand command,
        CancellationToken cancellationToken)
    {
        var ownerId = TodoAccessGuard.RequireCurrentUserId(_currentUserService);
        var job = await _jobRepository.GetByExternalIdAsync(command.JobExternalId, cancellationToken)
                  ?? throw new NotFoundException();

        if (job.CreatedByUserId != ownerId && !_currentUserService.HasAdminProfile)
        {
            throw new ForbiddenAccessException();
        }

        if (job.Status == AiBatchJobStatus.Completed && job.CreatedSprintId.HasValue)
        {
            return new ApproveSprintOptimizerProposalCommandResult
            {
                Payload = SprintOptimizerJobMapper.ToDto(job)
            };
        }

        if (!string.Equals(job.Status, AiBatchJobStatus.AwaitingApproval, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Job is not awaiting approval (status {job.Status}, phase {job.CurrentPhase}).");
        }

        var proposal = SprintOptimizerJobMapper.DeserializeProposal(job.ProposalJson)
                       ?? throw new InvalidOperationException("Proposal payload is missing or invalid.");

        var idsToUse = command.SelectedTaskExternalIds.Count > 0
            ? command.SelectedTaskExternalIds
            : proposal.SelectedTasks.Select(t => t.ExternalId).ToList();

        var proposalEdited = command.SelectedTaskExternalIds.Count > 0
            && !command.SelectedTaskExternalIds.SequenceEqual(proposal.SelectedTasks.Select(t => t.ExternalId));

        var selectedTodos = await _persistService.ResolveSelectedTodosAsync(
            job.CreatedByUserId,
            idsToUse,
            job.MaxTasks,
            cancellationToken);

        if (selectedTodos.Count == 0)
        {
            throw new InvalidOperationException("No valid tasks selected for sprint approval.");
        }

        var rejectedUnknownIds = idsToUse.Count - selectedTodos.Count;
        if (proposalEdited)
        {
            _telemetry.RecordQualityEvent(
                AiTelemetryNames.Features.SprintOptimizer,
                AiTelemetryNames.QualityEvents.ProposalEdited);
        }

        proposal.DecisionRecord = new SprintDecisionRecordDto
        {
            PromptVersion = AiPromptVersions.SprintOptimizer,
            ModelId = string.IsNullOrWhiteSpace(_aiConfig.Agents?.ChatModel)
                ? _aiConfig.Provider
                : _aiConfig.Agents.ChatModel,
            ToolInvocationCount = proposal.Steps.Count,
            RejectedUnknownIdCount = Math.Max(0, rejectedUnknownIds),
            UsedHeuristicFallback = proposal.UsedHeuristicFallback,
            ProposalEditedByUser = proposalEdited,
            ApprovalRejected = false
        };

        job.CurrentPhase = SprintOptimizerPhase.Persisting;
        job.StatusMessage = "Creating sprint from approved proposal…";
        job.Status = AiBatchJobStatus.Running;
        await _jobRepository.UpdateAsync(job, cancellationToken);
        await _notifier.NotifyAsync(SprintOptimizerJobMapper.ToDto(job), cancellationToken);

        var response = await _persistService.PersistApprovedAsync(
            job,
            selectedTodos,
            proposal.Reasoning,
            proposal.Steps,
            cancellationToken);

        job.ResultJson = SprintOptimizerJobMapper.SerializeResult(response);
        job.ProposalJson = null;
        job.CheckpointJson = null;
        job.PendingRequestId = null;
        job.Status = AiBatchJobStatus.Completed;
        job.CurrentPhase = SprintOptimizerPhase.Done;
        job.StatusMessage = $"Sprint created with {response.SelectedTasks.Count} tasks.";
        job.CompletedAt = DateTime.UtcNow;
        job.LastError = null;
        await _jobRepository.UpdateAsync(job, cancellationToken);

        var dto = SprintOptimizerJobMapper.ToDto(job);
        await _notifier.NotifyAsync(dto, cancellationToken);
        return new ApproveSprintOptimizerProposalCommandResult { Payload = dto };
    }
}

public sealed class RejectSprintOptimizerProposalCommandHandler
    : IRequestHandler<RejectSprintOptimizerProposalCommand, RejectSprintOptimizerProposalCommandResult>
{
    private readonly ISprintOptimizerJobRepository _jobRepository;
    private readonly ICurrentUserService _currentUserService;
    private readonly ISprintOptimizerNotifier _notifier;
    private readonly IAiTelemetry _telemetry;

    public RejectSprintOptimizerProposalCommandHandler(
        ISprintOptimizerJobRepository jobRepository,
        ICurrentUserService currentUserService,
        ISprintOptimizerNotifier notifier,
        IAiTelemetry? telemetry = null)
    {
        _jobRepository = jobRepository;
        _currentUserService = currentUserService;
        _notifier = notifier;
        _telemetry = telemetry ?? NullAiTelemetry.Instance;
    }

    public async Task<RejectSprintOptimizerProposalCommandResult> Handle(
        RejectSprintOptimizerProposalCommand command,
        CancellationToken cancellationToken)
    {
        var ownerId = TodoAccessGuard.RequireCurrentUserId(_currentUserService);
        var job = await _jobRepository.GetByExternalIdAsync(command.JobExternalId, cancellationToken)
                  ?? throw new NotFoundException();

        if (job.CreatedByUserId != ownerId && !_currentUserService.HasAdminProfile)
        {
            throw new ForbiddenAccessException();
        }

        if (job.Status == AiBatchJobStatus.Completed)
        {
            return new RejectSprintOptimizerProposalCommandResult
            {
                Payload = SprintOptimizerJobMapper.ToDto(job)
            };
        }

        if (!string.Equals(job.Status, AiBatchJobStatus.AwaitingApproval, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Job is not awaiting approval (status {job.Status}).");
        }

        _telemetry.RecordQualityEvent(
            AiTelemetryNames.Features.SprintOptimizer,
            AiTelemetryNames.QualityEvents.ApprovalRejected);

        job.Status = AiBatchJobStatus.Cancelled;
        job.CurrentPhase = SprintOptimizerPhase.Done;
        job.StatusMessage = "Proposal rejected by user.";
        job.ProposalJson = null;
        job.CheckpointJson = null;
        job.PendingRequestId = null;
        job.CompletedAt = DateTime.UtcNow;
        await _jobRepository.UpdateAsync(job, cancellationToken);

        var dto = SprintOptimizerJobMapper.ToDto(job);
        await _notifier.NotifyAsync(dto, cancellationToken);
        return new RejectSprintOptimizerProposalCommandResult { Payload = dto };
    }
}
