#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fistix.TaskManager.Core.DomainModel.Aggregates;

namespace Fistix.TaskManager.Core.Abstractions.Repositories;

public interface ISprintOptimizerJobRepository
{
    Task CreateAsync(SprintOptimizerJob job, CancellationToken cancellationToken);

    Task UpdateAsync(SprintOptimizerJob job, CancellationToken cancellationToken);

    Task<SprintOptimizerJob?> GetByExternalIdAsync(Guid externalId, CancellationToken cancellationToken);

    Task<SprintOptimizerJob?> GetActiveByOwnerAsync(Guid ownerExternalId, CancellationToken cancellationToken);

    Task<IReadOnlyList<SprintOptimizerJob>> GetRunnableAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<SprintOptimizerJob>> GetStaleRunningAsync(TimeSpan staleAfter, CancellationToken cancellationToken);
}
