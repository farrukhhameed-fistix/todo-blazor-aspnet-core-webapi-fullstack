using MediatR;
using System;
using System.Collections.Generic;

namespace Fistix.TaskManager.ViewModel.Commands.Todos;

public class AiQueryCommand : IRequest<AiQueryCommandResult>
{
    public string Question { get; set; } = string.Empty;
}

public class AiQueryCommandResult
{
    public AiQueryResponseDto Payload { get; set; } = new();
}

public class AiQuerySourceDto
{
    public Guid ExternalId { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateTime? DueDate { get; set; }
    public string Priority { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

public class AiQueryResponseDto
{
    public string Answer { get; set; } = string.Empty;
    public List<AiQuerySourceDto> Sources { get; set; } = new();
    public string Model { get; set; } = string.Empty;
}
