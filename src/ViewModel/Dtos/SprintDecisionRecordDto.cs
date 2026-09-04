#nullable enable

namespace Fistix.TaskManager.ViewModel.Dtos;

public sealed class SprintDecisionRecordDto
{
    public string PromptVersion { get; set; } = string.Empty;
    public string? ModelId { get; set; }
    public int ToolInvocationCount { get; set; }
    public int RejectedUnknownIdCount { get; set; }
    public bool UsedHeuristicFallback { get; set; }
    public bool ProposalEditedByUser { get; set; }
    public bool ApprovalRejected { get; set; }
}
