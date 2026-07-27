#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fistix.TaskManager.Core.DomainModel.Aggregates;

namespace Fistix.TaskManager.Core.Abstractions.Repositories;

public interface IAiBatchJobRepository
{
    Task CreateAsync(AiBatchJob job, CancellationToken cancellationToken);

    Task UpdateAsync(AiBatchJob job, CancellationToken cancellationToken);

    Task<AiBatchJob?> GetByExternalIdAsync(Guid externalId, CancellationToken cancellationToken);

    Task<AiBatchJob?> GetActiveByOwnerAsync(Guid ownerExternalId, CancellationToken cancellationToken);

    Task<IReadOnlyList<AiBatchJob>> GetRunnableAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<AiBatchJob>> GetStaleRunningAsync(TimeSpan staleAfter, CancellationToken cancellationToken);
}
