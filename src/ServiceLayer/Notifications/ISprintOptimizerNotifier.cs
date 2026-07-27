#nullable enable

using System.Threading;
using System.Threading.Tasks;
using Fistix.TaskManager.ViewModel.Dtos;

namespace Fistix.TaskManager.ServiceLayer.Notifications;

public interface ISprintOptimizerNotifier
{
    Task NotifyAsync(SprintOptimizerJobDto job, CancellationToken cancellationToken = default);
}

public sealed class NullSprintOptimizerNotifier : ISprintOptimizerNotifier
{
    public Task NotifyAsync(SprintOptimizerJobDto job, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
