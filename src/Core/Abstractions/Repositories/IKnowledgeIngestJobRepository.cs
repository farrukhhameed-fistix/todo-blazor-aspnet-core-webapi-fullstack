#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fistix.TaskManager.Core.DomainModel.Aggregates;

namespace Fistix.TaskManager.Core.Abstractions.Repositories;

public interface IKnowledgeIngestJobRepository
{
    Task CreateAsync(KnowledgeIngestJob job, CancellationToken cancellationToken);

    Task UpdateAsync(KnowledgeIngestJob job, CancellationToken cancellationToken);

    Task<KnowledgeIngestJob?> GetByExternalIdAsync(Guid externalId, CancellationToken cancellationToken);

    Task<KnowledgeIngestJob?> GetLatestByDocumentIdAsync(int documentId, CancellationToken cancellationToken);

    Task<IReadOnlyList<KnowledgeIngestJob>> GetRunnableAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<KnowledgeIngestJob>> GetStaleRunningAsync(TimeSpan staleAfter, CancellationToken cancellationToken);
}
