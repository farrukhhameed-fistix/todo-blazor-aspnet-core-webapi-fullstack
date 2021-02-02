using Fistix.TaskManager.Core.DomainModel.Aggregates;

namespace Fistix.TaskManager.Core.Abstractions.Services
{
  public interface ICurrentUserService
  {
    public string Email { get; }
    public bool HasAdminProfile { get; }
    public UserProfile UserProfile { get; }
  }
}
