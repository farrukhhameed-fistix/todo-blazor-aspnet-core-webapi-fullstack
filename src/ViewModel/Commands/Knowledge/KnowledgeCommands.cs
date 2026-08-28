#nullable enable

using System;
using Fistix.TaskManager.ViewModel.Dtos;
using MediatR;

namespace Fistix.TaskManager.ViewModel.Commands.Knowledge;

public class UploadKnowledgeDocumentCommand : IRequest<UploadKnowledgeDocumentCommandResult>
{
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}

public class UploadKnowledgeDocumentCommandResult
{
    public KnowledgeUploadResultDto Payload { get; set; } = new();
}

public class DeleteKnowledgeDocumentCommand : IRequest<DeleteKnowledgeDocumentCommandResult>
{
    public Guid DocumentExternalId { get; set; }
}

public class DeleteKnowledgeDocumentCommandResult
{
    public Guid DocumentExternalId { get; set; }
}

public class KnowledgeQueryCommand : IRequest<KnowledgeQueryCommandResult>
{
    public string Question { get; set; } = string.Empty;
    public Guid? DocumentExternalId { get; set; }
}

public class KnowledgeQueryCommandResult
{
    public KnowledgeQueryResponseDto Payload { get; set; } = new();
}
