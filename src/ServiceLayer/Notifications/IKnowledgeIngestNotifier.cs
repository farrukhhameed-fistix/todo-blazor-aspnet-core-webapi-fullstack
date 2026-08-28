#nullable enable

using System.Threading;
using System.Threading.Tasks;
using Fistix.TaskManager.ViewModel.Dtos;

namespace Fistix.TaskManager.ServiceLayer.Notifications;

public interface IKnowledgeIngestNotifier
{
    Task NotifyAsync(KnowledgeIngestJobDto job, CancellationToken cancellationToken = default);
}

public sealed class NullKnowledgeIngestNotifier : IKnowledgeIngestNotifier
{
    public Task NotifyAsync(KnowledgeIngestJobDto job, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
