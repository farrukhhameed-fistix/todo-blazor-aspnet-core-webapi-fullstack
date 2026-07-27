#nullable enable

using System;
using System.Threading.Tasks;
using Fistix.TaskManager.Core.Abstractions.Repositories;
using Fistix.TaskManager.Core.Abstractions.Services;
using Fistix.TaskManager.Core.SecurityModel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Fistix.TaskManager.WebApi.Hubs;

[Authorize]
public class AiBatchHub : Hub
{
    public const string HubPath = "/hubs/ai-batch";
    public const string BatchUpdatedMethod = "BatchJobUpdated";

    private readonly IAiBatchJobRepository _jobRepository;
    private readonly ICurrentUserService _currentUserService;

    public AiBatchHub(IAiBatchJobRepository jobRepository, ICurrentUserService currentUserService)
    {
        _jobRepository = jobRepository;
        _currentUserService = currentUserService;
    }

    public async Task JoinJob(Guid jobExternalId)
    {
        var job = await _jobRepository.GetByExternalIdAsync(jobExternalId, Context.ConnectionAborted)
                  ?? throw new HubException("Batch job not found.");

        var userId = TodoAccessGuard.RequireCurrentUserId(_currentUserService);
        if (job.CreatedByUserId != userId && !_currentUserService.HasAdminProfile)
        {
            throw new HubException("Forbidden.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, GetGroupName(jobExternalId));
    }

    public Task LeaveJob(Guid jobExternalId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, GetGroupName(jobExternalId));

    public static string GetGroupName(Guid jobExternalId) => $"ai-batch:{jobExternalId}";
}
