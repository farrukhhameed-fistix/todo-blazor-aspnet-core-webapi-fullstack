#nullable enable

using System;
using System.Threading.Tasks;
using Fistix.TaskManager.Core.Abstractions.Repositories;
using Fistix.TaskManager.Core.Abstractions.Services;
using Fistix.TaskManager.Core.Exceptions;
using Fistix.TaskManager.Core.SecurityModel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Fistix.TaskManager.WebApi.Hubs;

[Authorize]
public class SprintOptimizerHub : Hub
{
    public const string HubPath = "/hubs/sprint-optimizer";
    public const string JobUpdatedMethod = "SprintOptimizerUpdated";

    private readonly ISprintOptimizerJobRepository _jobRepository;
    private readonly ICurrentUserService _currentUserService;

    public SprintOptimizerHub(
        ISprintOptimizerJobRepository jobRepository,
        ICurrentUserService currentUserService)
    {
        _jobRepository = jobRepository;
        _currentUserService = currentUserService;
    }

    public async Task JoinJob(Guid jobExternalId)
    {
        try
        {
            var job = await _jobRepository.GetByExternalIdAsync(jobExternalId, Context.ConnectionAborted)
                      ?? throw new HubException("Sprint optimizer job not found.");

            var userId = TodoAccessGuard.RequireCurrentUserId(_currentUserService);
            if (job.CreatedByUserId != userId && !_currentUserService.HasAdminProfile)
            {
                throw new HubException("Forbidden.");
            }

            await Groups.AddToGroupAsync(Context.ConnectionId, GetGroupName(jobExternalId));
        }
        catch (ForbiddenAccessException)
        {
            throw new HubException("User profile not found or access denied.");
        }
    }

    public Task LeaveJob(Guid jobExternalId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, GetGroupName(jobExternalId));

    public static string GetGroupName(Guid jobExternalId) => $"sprint-optimizer:{jobExternalId}";
}
