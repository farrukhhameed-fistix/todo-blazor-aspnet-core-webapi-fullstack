#nullable enable

using System.Threading;
using System.Threading.Tasks;
using Fistix.TaskManager.ServiceLayer.Notifications;
using Fistix.TaskManager.ViewModel.Dtos;
using Fistix.TaskManager.WebApi.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace Fistix.TaskManager.WebApi.Notifications;

public sealed class SignalRSprintOptimizerNotifier : ISprintOptimizerNotifier
{
    private readonly IHubContext<SprintOptimizerHub> _hubContext;

    public SignalRSprintOptimizerNotifier(IHubContext<SprintOptimizerHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public Task NotifyAsync(SprintOptimizerJobDto job, CancellationToken cancellationToken = default) =>
        _hubContext.Clients
            .Group(SprintOptimizerHub.GetGroupName(job.ExternalId))
            .SendAsync(SprintOptimizerHub.JobUpdatedMethod, job, cancellationToken);
}
