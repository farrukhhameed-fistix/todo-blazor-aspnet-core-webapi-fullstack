#nullable enable

namespace Fistix.TaskManager.AiLayer.Models;

public sealed class RagPipelineRequest
{
    public string Question { get; set; } = string.Empty;
    /// <summary>
    /// When set, sources were already limited to this due-date window by the caller.
    /// The LLM must not re-interpret calendar phrases against other dates.
    /// </summary>
    public string? PreFilteredDateWindow { get; set; }
    public IReadOnlyList<RagSourceTodo> SourceTodos { get; set; } = Array.Empty<RagSourceTodo>();
}

public sealed class RagSourceTodo
{
    public Guid ExternalId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Priority { get; set; }
    public string? Status { get; set; }
    public DateTime DueDate { get; set; }
}

public sealed class RagPipelineResult
{
    public string Answer { get; set; } = string.Empty;
    public IReadOnlyList<Guid> SourceTodoIds { get; set; } = Array.Empty<Guid>();
    public string Model { get; set; } = string.Empty;
}
