using Fistix.TaskManager.Core.DomainModel.SeedWork;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Fistix.TaskManager.Core.DomainModel.Aggregates
{
  public class UserProfile: Entity
  {    
    public string Name { get; set; }
    public string EmailAddress { get; set; }
    public bool IsAdmin { get; set; }
  }
}
