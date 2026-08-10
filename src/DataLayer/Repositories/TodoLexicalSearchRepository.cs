#nullable enable

using Fistix.TaskManager.Core.Abstractions.Repositories;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Fistix.TaskManager.DataLayer.Repositories;

public sealed class TodoLexicalSearchRepository : ITodoLexicalSearchRepository
{
    private readonly EfContext _context;

    public TodoLexicalSearchRepository(EfContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<TodoLexicalSearchHit>> SearchAsync(
        string query,
        Guid? ownerExternalId,
        int limit,
        IReadOnlyCollection<Guid>? allowedExternalIds,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query) || limit <= 0)
        {
            return Array.Empty<TodoLexicalSearchHit>();
        }

        if (allowedExternalIds is { Count: 0 })
        {
            return Array.Empty<TodoLexicalSearchHit>();
        }

        var take = Math.Clamp(limit, 1, 200);
        var sql = """
            SELECT t."ExternalId" AS "TodoExternalId",
                   t."Id" AS "TodoId",
                   ts_rank(t."SearchVector", plainto_tsquery('english', @query)) AS "Rank"
            FROM "TodoTask" t
            WHERE t."SearchVector" @@ plainto_tsquery('english', @query)
            """;

        if (ownerExternalId.HasValue)
        {
            sql += """ AND t."CreatedByUserId" = @owner """;
        }

        if (allowedExternalIds is { Count: > 0 })
        {
            sql += """ AND t."ExternalId" = ANY(@allowed) """;
        }

        sql += """ ORDER BY "Rank" DESC LIMIT @limit """;

        var parameters = new List<NpgsqlParameter>
        {
            new("query", query),
            new("limit", take)
        };

        if (ownerExternalId.HasValue)
        {
            parameters.Add(new NpgsqlParameter("owner", ownerExternalId.Value));
        }

        if (allowedExternalIds is { Count: > 0 })
        {
            parameters.Add(new NpgsqlParameter("allowed", NpgsqlDbType.Array | NpgsqlDbType.Uuid)
            {
                Value = allowedExternalIds.ToArray()
            });
        }

        var rows = await _context.Database
            .SqlQueryRaw<LexicalHitRow>(sql, parameters.ToArray())
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new TodoLexicalSearchHit(r.TodoExternalId, r.TodoId, r.Rank))
            .ToList();
    }

    private sealed class LexicalHitRow
    {
        public Guid TodoExternalId { get; set; }
        public int TodoId { get; set; }
        public double Rank { get; set; }
    }
}
