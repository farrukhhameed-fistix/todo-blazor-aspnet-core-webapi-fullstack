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
public class KnowledgeIngestHub : Hub
{
    public const string HubPath = "/hubs/knowledge-ingest";
    public const string JobUpdatedMethod = "IngestJobUpdated";

    private readonly IKnowledgeIngestJobRepository _jobRepository;
    private readonly ICurrentUserService _currentUserService;

    public KnowledgeIngestHub(
        IKnowledgeIngestJobRepository jobRepository,
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
                      ?? throw new HubException("Ingest job not found.");

            KnowledgeAccessGuard.EnsureOwner(job, _currentUserService);
            await Groups.AddToGroupAsync(Context.ConnectionId, GetGroupName(jobExternalId));
        }
        catch (ForbiddenAccessException)
        {
            throw new HubException("Forbidden.");
        }
    }

    public Task LeaveJob(Guid jobExternalId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, GetGroupName(jobExternalId));

    public static string GetGroupName(Guid jobExternalId) => $"knowledge-ingest:{jobExternalId}";
}
