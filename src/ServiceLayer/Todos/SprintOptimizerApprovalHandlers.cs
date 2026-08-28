#nullable enable

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fistix.TaskManager.Core.Abstractions.Repositories;
using Fistix.TaskManager.Core.Abstractions.Services;
using Fistix.TaskManager.Core.DomainModel.Constants;
using Fistix.TaskManager.Core.Exceptions;
using Fistix.TaskManager.Core.SecurityModel;
using Fistix.TaskManager.ServiceLayer.Notifications;
using Fistix.TaskManager.ViewModel.Commands.Todos;
using MediatR;

namespace Fistix.TaskManager.ServiceLayer.Todos;

public sealed class ApproveSprintOptimizerProposalCommandHandler
    : IRequestHandler<ApproveSprintOptimizerProposalCommand, ApproveSprintOptimizerProposalCommandResult>
{
    private readonly ISprintOptimizerJobRepository _jobRepository;
    private readonly ICurrentUserService _currentUserService;
    private readonly ISprintOptimizerNotifier _notifier;
    private readonly SprintOptimizerPersistService _persistService;

    public ApproveSprintOptimizerProposalCommandHandler(
        ISprintOptimizerJobRepository jobRepository,
        ICurrentUserService currentUserService,
        ISprintOptimizerNotifier notifier,
        SprintOptimizerPersistService persistService)
    {
        _jobRepository = jobRepository;
        _currentUserService = currentUserService;
        _notifier = notifier;
        _persistService = persistService;
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

        var selectedTodos = await _persistService.ResolveSelectedTodosAsync(
            job.CreatedByUserId,
            idsToUse,
            job.MaxTasks,
            cancellationToken);

        if (selectedTodos.Count == 0)
        {
            throw new InvalidOperationException("No valid tasks selected for sprint approval.");
        }

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

    public RejectSprintOptimizerProposalCommandHandler(
        ISprintOptimizerJobRepository jobRepository,
        ICurrentUserService currentUserService,
        ISprintOptimizerNotifier notifier)
    {
        _jobRepository = jobRepository;
        _currentUserService = currentUserService;
        _notifier = notifier;
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

        job.Status = AiBatchJobStatus.Cancelled;
        job.CurrentPhase = SprintOptimizerPhase.Done;
        job.StatusMessage = "Proposal rejected by user.";
        job.ProposalJson = null;
        job.CompletedAt = DateTime.UtcNow;
        await _jobRepository.UpdateAsync(job, cancellationToken);

        var dto = SprintOptimizerJobMapper.ToDto(job);
        await _notifier.NotifyAsync(dto, cancellationToken);
        return new RejectSprintOptimizerProposalCommandResult { Payload = dto };
    }
}
