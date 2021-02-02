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
  }
}
