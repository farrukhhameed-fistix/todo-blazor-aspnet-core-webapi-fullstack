#nullable enable

using System.Threading;
using System.Threading.Tasks;
using Fistix.TaskManager.ServiceLayer.Notifications;
using Fistix.TaskManager.ViewModel.Dtos;
using Fistix.TaskManager.WebApi.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace Fistix.TaskManager.WebApi.Notifications;

public sealed class SignalRAiBatchNotifier : IAiBatchNotifier
{
    private readonly IHubContext<AiBatchHub> _hubContext;

    public SignalRAiBatchNotifier(IHubContext<AiBatchHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public Task NotifyAsync(AiBatchJobDto job, CancellationToken cancellationToken = default) =>
        _hubContext.Clients
            .Group(AiBatchHub.GetGroupName(job.ExternalId))
            .SendAsync(AiBatchHub.BatchUpdatedMethod, job, cancellationToken);
}
