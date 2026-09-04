#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fistix.TaskManager.Core.DomainModel.Aggregates;

namespace Fistix.TaskManager.Core.Abstractions.Repositories;

public interface IKnowledgeDocumentRepository
{
    Task CreateAsync(KnowledgeDocument document, CancellationToken cancellationToken);

    Task UpdateAsync(KnowledgeDocument document, CancellationToken cancellationToken);

    Task<KnowledgeDocument?> GetByExternalIdAsync(Guid externalId, CancellationToken cancellationToken);

    Task<KnowledgeDocument?> GetByIdAsync(int id, CancellationToken cancellationToken);

    Task<IReadOnlyList<KnowledgeDocument>> ListByOwnerAsync(Guid ownerExternalId, CancellationToken cancellationToken);

    Task<KnowledgeDocument?> FindByOwnerAndFileNameAsync(
        Guid ownerExternalId,
        string fileName,
        CancellationToken cancellationToken);

    Task DeleteAsync(KnowledgeDocument document, CancellationToken cancellationToken);
}
