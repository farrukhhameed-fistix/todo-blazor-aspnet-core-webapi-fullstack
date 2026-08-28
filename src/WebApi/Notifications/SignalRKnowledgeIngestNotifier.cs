#nullable enable

using System.Threading;
using System.Threading.Tasks;
using Fistix.TaskManager.ServiceLayer.Notifications;
using Fistix.TaskManager.ViewModel.Dtos;
using Fistix.TaskManager.WebApi.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace Fistix.TaskManager.WebApi.Notifications;

public sealed class SignalRKnowledgeIngestNotifier : IKnowledgeIngestNotifier
{
    private readonly IHubContext<KnowledgeIngestHub> _hubContext;

    public SignalRKnowledgeIngestNotifier(IHubContext<KnowledgeIngestHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public Task NotifyAsync(KnowledgeIngestJobDto job, CancellationToken cancellationToken = default) =>
        _hubContext.Clients
            .Group(KnowledgeIngestHub.GetGroupName(job.ExternalId))
            .SendAsync(KnowledgeIngestHub.JobUpdatedMethod, job, cancellationToken);
}
