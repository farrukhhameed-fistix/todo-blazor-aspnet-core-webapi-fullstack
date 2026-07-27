#nullable enable

using System.Threading;
using System.Threading.Tasks;
using Fistix.TaskManager.ViewModel.Dtos;

namespace Fistix.TaskManager.ServiceLayer.Notifications;

public interface IAiBatchNotifier
{
    Task NotifyAsync(AiBatchJobDto job, CancellationToken cancellationToken = default);
}

/// <summary>No-op for tests and hosts without SignalR.</summary>
public sealed class NullAiBatchNotifier : IAiBatchNotifier
{
    public Task NotifyAsync(AiBatchJobDto job, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
