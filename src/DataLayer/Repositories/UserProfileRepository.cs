using Fistix.TaskManager.Core.Abstractions.Repositories;
using Fistix.TaskManager.Core.DomainModel.Aggregates;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Fistix.TaskManager.DataLayer.Repositories
{
  public class UserProfileRepository: IUserProfileRepository
  {
    private readonly EfContext _context;

    public UserProfileRepository(EfContext context)
    {
      _context = context;
    }

    public async Task<UserProfile> GetByEmailAddress(string emailAddress)
    {
      return await _context.UserProfiles.FirstOrDefaultAsync(x => x.EmailAddress == emailAddress);
    }
  }
}
