#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fistix.TaskManager.Core.Abstractions.Repositories;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Fistix.TaskManager.DataLayer.Repositories;

public sealed class KnowledgeLexicalSearchRepository : IKnowledgeLexicalSearchRepository
{
    private readonly EfContext _context;

    public KnowledgeLexicalSearchRepository(EfContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<KnowledgeLexicalSearchHit>> SearchAsync(
        string query,
        Guid ownerExternalId,
        int limit,
        CancellationToken cancellationToken,
        Guid? documentExternalId = null,
        IReadOnlyCollection<Guid>? excludeChunkExternalIds = null)
    {
        if (string.IsNullOrWhiteSpace(query) || limit <= 0)
        {
            return Array.Empty<KnowledgeLexicalSearchHit>();
        }

        var take = Math.Clamp(limit, 1, 200);
        var sql = """
            SELECT c."ExternalId" AS "ChunkExternalId",
                   c."Id" AS "ChunkId",
                   d."ExternalId" AS "DocumentExternalId",
                   d."FileName" AS "FileName",
                   c."Ordinal" AS "Ordinal",
                   c."Content" AS "Content",
                   c."Heading" AS "Heading",
                   ts_rank(c."SearchVector", plainto_tsquery('english', @query)) AS "Rank"
            FROM "KnowledgeChunk" c
            INNER JOIN "KnowledgeDocument" d ON d."Id" = c."DocumentId"
            WHERE d."CreatedByUserId" = @owner
              AND c."SearchVector" @@ plainto_tsquery('english', @query)
            """;

        if (documentExternalId.HasValue)
        {
            sql += """ AND d."ExternalId" = @document """;
        }

        if (excludeChunkExternalIds is { Count: > 0 })
        {
            sql += """ AND c."ExternalId" <> ALL(@exclude) """;
        }

        sql += """ ORDER BY "Rank" DESC LIMIT @limit """;

        var parameters = new List<NpgsqlParameter>
        {
            new("query", query),
            new("owner", ownerExternalId),
            new("limit", take)
        };

        if (documentExternalId.HasValue)
        {
            parameters.Add(new NpgsqlParameter("document", documentExternalId.Value));
        }

        if (excludeChunkExternalIds is { Count: > 0 })
        {
            parameters.Add(new NpgsqlParameter("exclude", NpgsqlDbType.Array | NpgsqlDbType.Uuid)
            {
                Value = excludeChunkExternalIds.ToArray()
            });
        }

        var rows = await _context.Database
            .SqlQueryRaw<LexicalHitRow>(sql, parameters.ToArray())
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new KnowledgeLexicalSearchHit(
                r.ChunkExternalId,
                r.ChunkId,
                r.DocumentExternalId,
                r.FileName ?? string.Empty,
                r.Ordinal,
                r.Content ?? string.Empty,
                r.Heading,
                r.Rank))
            .ToList();
    }

    private sealed class LexicalHitRow
    {
        public Guid ChunkExternalId { get; set; }
        public int ChunkId { get; set; }
        public Guid DocumentExternalId { get; set; }
        public string? FileName { get; set; }
        public int Ordinal { get; set; }
        public string? Content { get; set; }
        public string? Heading { get; set; }
        public double Rank { get; set; }
    }
}
