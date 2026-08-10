#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Fistix.TaskManager.Core.Abstractions.Repositories;

public interface ITodoLexicalSearchRepository
{
    Task<IReadOnlyList<TodoLexicalSearchHit>> SearchAsync(
        string query,
        Guid? ownerExternalId,
        int limit,
        IReadOnlyCollection<Guid>? allowedExternalIds,
        CancellationToken cancellationToken);
}

public sealed record TodoLexicalSearchHit(Guid TodoExternalId, int TodoId, double Rank);
