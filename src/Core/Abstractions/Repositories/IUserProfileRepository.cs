using Fistix.TaskManager.Core.DomainModel.Aggregates;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Fistix.TaskManager.Core.Abstractions.Repositories
{
  public interface IUserProfileRepository
  {
    Task<UserProfile> GetByEmailAddress(string emailAddress);
  }
}
