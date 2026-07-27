using Fistix.TaskManager.Core.DomainModel.SeedWork;
using System;
using System.Collections.Generic;
using System.Text;

namespace Fistix.TaskManager.Core.DomainModel.Aggregates
{
  public class TodoTask : Entity
  {
    public string Title { get; set; }
    public string Description { get; set; }
    public DateTime DueDate { get; set; }
    public DateTime CreatedOn { get; set; }
    public string Status { get; set; } = "Pending";
    public string Priority { get; set; } = "Medium";
    public string Category { get; set; }
    public string AssignedTo { get; set; }
    public Guid CreatedByUserId { get; set; }

    /// <summary>Optional tag for CSV/eval imports so a set can be deleted and re-imported.</summary>
    public string? ImportTag { get; set; }

    public virtual TodoAiMetadata? AiMetadata { get; set; }
  }
}
