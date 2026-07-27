using System;

namespace Fistix.TaskManager.ViewModel.Dtos;

/// <summary>
/// DTO for task priority classification response from AI API.
/// </summary>
public class TaskClassificationDto
{
    public Guid TodoExternalId { get; set; }
    public string? SuggestedPriority { get; set; }
    public float? Confidence { get; set; }
    public string? Reason { get; set; }
    /// <summary>None, Pending, Completed, or Failed. Defaults to None (not analyzing).</summary>
    public string Status { get; set; } = "None";
    public string? Model { get; set; }
    public bool FromCache { get; set; }
    public DateTime? GeneratedAt { get; set; }
}
