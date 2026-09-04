#nullable enable

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fistix.TaskManager.AiLayer.Shared;
using Fistix.TaskManager.Core.Abstractions.Repositories;
using Fistix.TaskManager.Core.Abstractions.Services;
using Fistix.TaskManager.Core.DomainModel.Aggregates;
using Fistix.TaskManager.Core.DomainModel.Constants;
using Fistix.TaskManager.Core.Exceptions;
using Fistix.TaskManager.Core.SecurityModel;
using Fistix.TaskManager.ServiceLayer.Notifications;
using Fistix.TaskManager.ViewModel.Commands.Todos;
using Fistix.TaskManager.ViewModel.Dtos;
using Fistix.TaskManager.ViewModel.Queries.Todos;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Fistix.TaskManager.ServiceLayer.Todos;

public sealed class OptimizeSprintCommandHandler : IRequestHandler<OptimizeSprintCommand, OptimizeSprintCommandResult>
{
    private readonly ISprintOptimizerJobRepository _jobRepository;
    private readonly ICurrentUserService _currentUserService;
    private readonly AiConfiguration _aiConfig;
    private readonly ISprintOptimizerNotifier _notifier;
    private readonly ILogger<OptimizeSprintCommandHandler> _logger;

    public OptimizeSprintCommandHandler(
        ISprintOptimizerJobRepository jobRepository,
        ICurrentUserService currentUserService,
        AiConfiguration aiConfig,
        ISprintOptimizerNotifier notifier,
        ILogger<OptimizeSprintCommandHandler> logger)
    {
        _jobRepository = jobRepository;
        _currentUserService = currentUserService;
        _aiConfig = aiConfig;
        _notifier = notifier;
        _logger = logger;
    }

    public async Task<OptimizeSprintCommandResult> Handle(
        OptimizeSprintCommand command,
        CancellationToken cancellationToken)
    {
        if (!_aiConfig.Features.EnableAgents)
        {
            throw new FeatureDisabledException("AI sprint optimizer agent");
        }

        var userId = TodoAccessGuard.RequireCurrentUserId(_currentUserService);
        var active = await _jobRepository.GetActiveByOwnerAsync(userId, cancellationToken);
        if (active is not null)
        {
            throw new InvalidOperationException(
                $"An active sprint optimizer job already exists ({active.ExternalId}, status {active.Status}). Cancel it before starting a new one.");
        }

        var job = new SprintOptimizerJob
        {
            CreatedByUserId = userId,
            MaxTasks = Math.Clamp(command.MaxTasks, 1, 50),
            DurationDays = Math.Clamp(command.DurationDays, 1, 90),
            Name = string.IsNullOrWhiteSpace(command.Name) ? null : command.Name.Trim(),
            Status = AiBatchJobStatus.Pending,
            CurrentPhase = SprintOptimizerPhase.Queued,
            StatusMessage = "Queued for sprint planning.",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            HeartbeatAt = DateTime.UtcNow
        };
        job.GenerateNewExternalId();

        await _jobRepository.CreateAsync(job, cancellationToken);
        _logger.LogInformation(
            "Queued sprint optimizer job {JobId} for user {UserId} (maxTasks={MaxTasks})",
            job.ExternalId,
            userId,
            job.MaxTasks);

        var dto = SprintOptimizerJobMapper.ToDto(job);
        await _notifier.NotifyAsync(dto, cancellationToken);
        return new OptimizeSprintCommandResult { Payload = dto };
    }
}

public sealed class CancelSprintOptimizerJobCommandHandler
    : IRequestHandler<CancelSprintOptimizerJobCommand, CancelSprintOptimizerJobCommandResult>
{
    private readonly ISprintOptimizerJobRepository _jobRepository;
    private readonly ICurrentUserService _currentUserService;
    private readonly ISprintOptimizerNotifier _notifier;

    public CancelSprintOptimizerJobCommandHandler(
        ISprintOptimizerJobRepository jobRepository,
        ICurrentUserService currentUserService,
        ISprintOptimizerNotifier notifier)
    {
        _jobRepository = jobRepository;
        _currentUserService = currentUserService;
        _notifier = notifier;
    }

    public async Task<CancelSprintOptimizerJobCommandResult> Handle(
        CancelSprintOptimizerJobCommand command,
        CancellationToken cancellationToken)
    {
        var ownerId = TodoAccessGuard.RequireCurrentUserId(_currentUserService);
        var job = await _jobRepository.GetByExternalIdAsync(command.JobExternalId, cancellationToken)
                  ?? throw new NotFoundException();
        if (job.CreatedByUserId != ownerId && !_currentUserService.HasAdminProfile)
        {
            throw new ForbiddenAccessException();
        }

        if (job.Status is AiBatchJobStatus.Completed or AiBatchJobStatus.Cancelled)
        {
            return new CancelSprintOptimizerJobCommandResult { Payload = SprintOptimizerJobMapper.ToDto(job) };
        }

        if (job.Status is AiBatchJobStatus.AwaitingApproval)
        {
            job.ProposalJson = null;
        }

        job.CancelRequested = true;
        job.Status = AiBatchJobStatus.Cancelled;
        job.StatusMessage = "Cancelled by user.";
        job.CompletedAt = DateTime.UtcNow;
        await _jobRepository.UpdateAsync(job, cancellationToken);
        var dto = SprintOptimizerJobMapper.ToDto(job);
        await _notifier.NotifyAsync(dto, cancellationToken);
        return new CancelSprintOptimizerJobCommandResult { Payload = dto };
    }
}

public sealed class GetSprintOptimizerJobQueryHandler
    : IRequestHandler<GetSprintOptimizerJobQuery, GetSprintOptimizerJobQueryResult>
{
    private readonly ISprintOptimizerJobRepository _jobRepository;
    private readonly ICurrentUserService _currentUserService;

    public GetSprintOptimizerJobQueryHandler(
        ISprintOptimizerJobRepository jobRepository,
        ICurrentUserService currentUserService)
    {
        _jobRepository = jobRepository;
        _currentUserService = currentUserService;
    }

    public async Task<GetSprintOptimizerJobQueryResult> Handle(
        GetSprintOptimizerJobQuery query,
        CancellationToken cancellationToken)
    {
        var ownerId = TodoAccessGuard.RequireCurrentUserId(_currentUserService);
        var job = await _jobRepository.GetByExternalIdAsync(query.JobExternalId, cancellationToken)
                  ?? throw new NotFoundException();
        if (job.CreatedByUserId != ownerId && !_currentUserService.HasAdminProfile)
        {
            throw new ForbiddenAccessException();
        }

        return new GetSprintOptimizerJobQueryResult { Payload = SprintOptimizerJobMapper.ToDto(job) };
    }
}

public sealed class GetActiveSprintOptimizerJobQueryHandler
    : IRequestHandler<GetActiveSprintOptimizerJobQuery, GetActiveSprintOptimizerJobQueryResult>
{
    private readonly ISprintOptimizerJobRepository _jobRepository;
    private readonly ICurrentUserService _currentUserService;

    public GetActiveSprintOptimizerJobQueryHandler(
        ISprintOptimizerJobRepository jobRepository,
        ICurrentUserService currentUserService)
    {
        _jobRepository = jobRepository;
        _currentUserService = currentUserService;
    }

    public async Task<GetActiveSprintOptimizerJobQueryResult> Handle(
        GetActiveSprintOptimizerJobQuery query,
        CancellationToken cancellationToken)
    {
        var ownerId = TodoAccessGuard.RequireCurrentUserId(_currentUserService);
        var job = await _jobRepository.GetActiveByOwnerAsync(ownerId, cancellationToken);
        return new GetActiveSprintOptimizerJobQueryResult
        {
            Payload = job is null ? null : SprintOptimizerJobMapper.ToDto(job)
        };
    }
}
