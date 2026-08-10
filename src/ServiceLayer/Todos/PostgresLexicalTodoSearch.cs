#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fistix.TaskManager.AiLayer.Abstractions;
using Fistix.TaskManager.Core.Abstractions.Repositories;

namespace Fistix.TaskManager.ServiceLayer.Todos;

public sealed class PostgresLexicalTodoSearch : ILexicalTodoSearch
{
    private readonly ITodoLexicalSearchRepository _repository;

    public PostgresLexicalTodoSearch(ITodoLexicalSearchRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<LexicalSearchHit>> SearchAsync(
        string query,
        Guid? ownerExternalId,
        int limit,
        IReadOnlyCollection<Guid>? allowedExternalIds = null,
        CancellationToken cancellationToken = default)
    {
        var hits = await _repository.SearchAsync(
            query,
            ownerExternalId,
            limit,
            allowedExternalIds,
            cancellationToken);

        return hits
            .Select(h => new LexicalSearchHit(h.TodoExternalId, h.TodoId, h.Rank))
            .ToList();
    }
}
