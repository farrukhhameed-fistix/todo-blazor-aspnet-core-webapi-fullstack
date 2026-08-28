#nullable enable

using System;
using System.Collections.Generic;
using Fistix.TaskManager.ViewModel.Dtos;
using MediatR;

namespace Fistix.TaskManager.ViewModel.Queries.Knowledge;

public class ListKnowledgeDocumentsQuery : IRequest<ListKnowledgeDocumentsQueryResult>
{
}

public class ListKnowledgeDocumentsQueryResult
{
    public List<KnowledgeDocumentDto> Payload { get; set; } = [];
}

public class GetKnowledgeDocumentQuery : IRequest<GetKnowledgeDocumentQueryResult>
{
    public Guid DocumentExternalId { get; set; }
}

public class GetKnowledgeDocumentQueryResult
{
    public KnowledgeDocumentDto Payload { get; set; } = new();
}

public class ListKnowledgeChunksQuery : IRequest<ListKnowledgeChunksQueryResult>
{
    public Guid DocumentExternalId { get; set; }
}

public class ListKnowledgeChunksQueryResult
{
    public List<KnowledgeChunkDto> Payload { get; set; } = [];
}

public class GetKnowledgeIngestJobQuery : IRequest<GetKnowledgeIngestJobQueryResult>
{
    public Guid JobExternalId { get; set; }
}

public class GetKnowledgeIngestJobQueryResult
{
    public KnowledgeIngestJobDto Payload { get; set; } = new();
}
