#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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

public sealed class StartAiBatchJobCommandHandler
    : IRequestHandler<StartAiBatchJobCommand, StartAiBatchJobCommandResult>
{
    private readonly IAiBatchJobRepository _jobRepository;
    private readonly ITodoTaskRepository _todoTaskRepository;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<StartAiBatchJobCommandHandler> _logger;

    public StartAiBatchJobCommandHandler(
        IAiBatchJobRepository jobRepository,
        ITodoTaskRepository todoTaskRepository,
        ICurrentUserService currentUserService,
        ILogger<StartAiBatchJobCommandHandler> logger)
    {
        _jobRepository = jobRepository;
        _todoTaskRepository = todoTaskRepository;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    public async Task<StartAiBatchJobCommandResult> Handle(
        StartAiBatchJobCommand command,
        CancellationToken cancellationToken)
    {
        var ownerId = TodoAccessGuard.RequireCurrentUserId(_currentUserService);

        var active = await _jobRepository.GetActiveByOwnerAsync(ownerId, cancellationToken);
        if (active is not null)
        {
            throw new InvalidOperationException(
                $"An active AI batch job already exists ({active.ExternalId}, status {active.Status}). Cancel it before starting a new one.");
        }

        List<Guid> todoIds;
        if (!string.IsNullOrWhiteSpace(command.ImportTag))
        {
            var todos = await _todoTaskRepository.GetByOwnerAndImportTagAsync(
                ownerId,
                command.ImportTag.Trim(),
                cancellationToken);
            todoIds = todos.Select(t => t.ExternalId).ToList();
        }
        else
        {
            todoIds = command.TodoExternalIds?.Distinct().ToList() ?? [];
            foreach (var id in todoIds)
            {
                var todo = await _todoTaskRepository.Get(id, cancellationToken);
                TodoAccessGuard.EnsureCanAccess(todo, _currentUserService);
            }
        }

        if (todoIds.Count == 0)
        {
            throw new InvalidOperationException("No todos found to process.");
        }

        var steps = command.Steps is { Count: > 0 }
            ? command.Steps.Select(AiBatchStepNames.Normalize).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            : AiBatchStepNames.DefaultSteps.ToList();

        var job = new AiBatchJob
        {
            CreatedByUserId = ownerId,
            StepsCsv = AiBatchJobMapper.StepsToCsv(steps),
            CurrentStep = steps[0],
            TodoExternalIdsJson = AiBatchJobMapper.SerializeIds(todoIds),
            Cursor = 0,
            Total = todoIds.Count,
            BatchSize = Math.Clamp(command.BatchSize, 1, 50),
            DelayMsBetweenItems = Math.Clamp(command.DelayMsBetweenItems, 0, 60_000),
            OnlyMissing = command.OnlyMissing,
            Status = AiBatchJobStatus.Running,
            ImportTag = string.IsNullOrWhiteSpace(command.ImportTag) ? null : command.ImportTag.Trim(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            HeartbeatAt = DateTime.UtcNow
        };
        job.GenerateNewExternalId();

        await _jobRepository.CreateAsync(job, cancellationToken);
        _logger.LogInformation(
            "Started AI batch job {JobId} for {Count} todos, steps={Steps}",
            job.ExternalId,
            todoIds.Count,
            job.StepsCsv);

        return new StartAiBatchJobCommandResult { Payload = AiBatchJobMapper.ToDto(job) };
    }
}

public sealed class PauseAiBatchJobCommandHandler : IRequestHandler<PauseAiBatchJobCommand, AiBatchJobCommandResult>
{
    private readonly IAiBatchJobRepository _jobRepository;
    private readonly ICurrentUserService _currentUserService;
    private readonly IAiBatchNotifier _notifier;

    public PauseAiBatchJobCommandHandler(
        IAiBatchJobRepository jobRepository,
        ICurrentUserService currentUserService,
        IAiBatchNotifier notifier)
    {
        _jobRepository = jobRepository;
        _currentUserService = currentUserService;
        _notifier = notifier;
    }

    public async Task<AiBatchJobCommandResult> Handle(PauseAiBatchJobCommand command, CancellationToken cancellationToken)
    {
        var job = await LoadOwnedJobAsync(command.JobExternalId, cancellationToken);
        if (job.Status is not (AiBatchJobStatus.Running or AiBatchJobStatus.Pending or AiBatchJobStatus.Stuck))
        {
            throw new InvalidOperationException($"Cannot pause job in status {job.Status}.");
        }

        job.Status = AiBatchJobStatus.Paused;
        job.PausedAt = DateTime.UtcNow;
        await _jobRepository.UpdateAsync(job, cancellationToken);
        var dto = AiBatchJobMapper.ToDto(job);
        await _notifier.NotifyAsync(dto, cancellationToken);
        return new AiBatchJobCommandResult { Payload = dto };
    }

    private async Task<AiBatchJob> LoadOwnedJobAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var ownerId = TodoAccessGuard.RequireCurrentUserId(_currentUserService);
        var job = await _jobRepository.GetByExternalIdAsync(jobId, cancellationToken)
                  ?? throw new NotFoundException();
        if (job.CreatedByUserId != ownerId && !_currentUserService.HasAdminProfile)
        {
            throw new ForbiddenAccessException();
        }

        return job;
    }
}

public sealed class ContinueAiBatchJobCommandHandler : IRequestHandler<ContinueAiBatchJobCommand, AiBatchJobCommandResult>
{
    private readonly IAiBatchJobRepository _jobRepository;
    private readonly ICurrentUserService _currentUserService;
    private readonly IAiBatchNotifier _notifier;

    public ContinueAiBatchJobCommandHandler(
        IAiBatchJobRepository jobRepository,
        ICurrentUserService currentUserService,
        IAiBatchNotifier notifier)
    {
        _jobRepository = jobRepository;
        _currentUserService = currentUserService;
        _notifier = notifier;
    }

    public async Task<AiBatchJobCommandResult> Handle(ContinueAiBatchJobCommand command, CancellationToken cancellationToken)
    {
        var ownerId = TodoAccessGuard.RequireCurrentUserId(_currentUserService);
        var job = await _jobRepository.GetByExternalIdAsync(command.JobExternalId, cancellationToken)
                  ?? throw new NotFoundException();
        if (job.CreatedByUserId != ownerId && !_currentUserService.HasAdminProfile)
        {
            throw new ForbiddenAccessException();
        }

        if (job.Status is not (AiBatchJobStatus.Paused or AiBatchJobStatus.Stuck or AiBatchJobStatus.Failed))
        {
            throw new InvalidOperationException($"Cannot continue job in status {job.Status}.");
        }

        job.Status = AiBatchJobStatus.Running;
        job.CancelRequested = false;
        job.PausedAt = null;
        job.LastError = null;
        job.HeartbeatAt = DateTime.UtcNow;
        await _jobRepository.UpdateAsync(job, cancellationToken);
        var dto = AiBatchJobMapper.ToDto(job);
        await _notifier.NotifyAsync(dto, cancellationToken);
        return new AiBatchJobCommandResult { Payload = dto };
    }
}

public sealed class CancelAiBatchJobCommandHandler : IRequestHandler<CancelAiBatchJobCommand, AiBatchJobCommandResult>
{
    private readonly IAiBatchJobRepository _jobRepository;
    private readonly ICurrentUserService _currentUserService;
    private readonly IAiBatchNotifier _notifier;

    public CancelAiBatchJobCommandHandler(
        IAiBatchJobRepository jobRepository,
        ICurrentUserService currentUserService,
        IAiBatchNotifier notifier)
    {
        _jobRepository = jobRepository;
        _currentUserService = currentUserService;
        _notifier = notifier;
    }

    public async Task<AiBatchJobCommandResult> Handle(CancelAiBatchJobCommand command, CancellationToken cancellationToken)
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
            return new AiBatchJobCommandResult { Payload = AiBatchJobMapper.ToDto(job) };
        }

        job.CancelRequested = true;
        job.Status = AiBatchJobStatus.Cancelled;
        job.CompletedAt = DateTime.UtcNow;
        await _jobRepository.UpdateAsync(job, cancellationToken);
        var dto = AiBatchJobMapper.ToDto(job);
        await _notifier.NotifyAsync(dto, cancellationToken);
        return new AiBatchJobCommandResult { Payload = dto };
    }
}

public sealed class GetAiBatchJobQueryHandler : IRequestHandler<GetAiBatchJobQuery, GetAiBatchJobQueryResult>
{
    private readonly IAiBatchJobRepository _jobRepository;
    private readonly ICurrentUserService _currentUserService;

    public GetAiBatchJobQueryHandler(IAiBatchJobRepository jobRepository, ICurrentUserService currentUserService)
    {
        _jobRepository = jobRepository;
        _currentUserService = currentUserService;
    }

    public async Task<GetAiBatchJobQueryResult> Handle(GetAiBatchJobQuery query, CancellationToken cancellationToken)
    {
        var ownerId = TodoAccessGuard.RequireCurrentUserId(_currentUserService);
        var job = await _jobRepository.GetByExternalIdAsync(query.JobExternalId, cancellationToken)
                  ?? throw new NotFoundException();
        if (job.CreatedByUserId != ownerId && !_currentUserService.HasAdminProfile)
        {
            throw new ForbiddenAccessException();
        }

        return new GetAiBatchJobQueryResult { Payload = AiBatchJobMapper.ToDto(job) };
    }
}

public sealed class GetActiveAiBatchJobQueryHandler : IRequestHandler<GetActiveAiBatchJobQuery, GetActiveAiBatchJobQueryResult>
{
    private readonly IAiBatchJobRepository _jobRepository;
    private readonly ICurrentUserService _currentUserService;

    public GetActiveAiBatchJobQueryHandler(IAiBatchJobRepository jobRepository, ICurrentUserService currentUserService)
    {
        _jobRepository = jobRepository;
        _currentUserService = currentUserService;
    }

    public async Task<GetActiveAiBatchJobQueryResult> Handle(
        GetActiveAiBatchJobQuery query,
        CancellationToken cancellationToken)
    {
        var ownerId = TodoAccessGuard.RequireCurrentUserId(_currentUserService);
        var job = await _jobRepository.GetActiveByOwnerAsync(ownerId, cancellationToken);
        return new GetActiveAiBatchJobQueryResult
        {
            Payload = job is null ? null : AiBatchJobMapper.ToDto(job)
        };
    }
}
