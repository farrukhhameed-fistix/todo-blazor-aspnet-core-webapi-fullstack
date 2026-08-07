#nullable enable

using System;

namespace Fistix.TaskManager.Core.DomainModel.Aggregates;

public class AiConversation
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string Query { get; set; } = string.Empty;
    public string Response { get; set; } = string.Empty;
    public string? ContextTodosJson { get; set; }
    public string? Model { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
