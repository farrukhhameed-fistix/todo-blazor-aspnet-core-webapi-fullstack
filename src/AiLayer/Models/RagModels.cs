#nullable enable

namespace Fistix.TaskManager.AiLayer.Models;

public enum RagCorpusKind
{
    Todos = 0,
    Knowledge = 1
}

public sealed class RagPipelineRequest
{
    public string Question { get; set; } = string.Empty;
    /// <summary>
    /// When set, sources were already limited to this due-date window by the caller.
    /// The LLM must not re-interpret calendar phrases against other dates.
    /// </summary>
    public string? PreFilteredDateWindow { get; set; }

    /// <summary>When set, caller already filtered to this priority (High/Medium/Low).</summary>
    public string? PreFilteredPriority { get; set; }

    /// <summary>True when the user asked for recommendations / what to work on next.</summary>
    public bool IsAdviceQuestion { get; set; }

    public RagCorpusKind CorpusKind { get; set; } = RagCorpusKind.Todos;

    public IReadOnlyList<RagSourceTodo> SourceTodos { get; set; } = Array.Empty<RagSourceTodo>();

    public IReadOnlyList<RagSource> Sources { get; set; } = Array.Empty<RagSource>();
}

public sealed class RagSource
{
    public Guid ExternalId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
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
