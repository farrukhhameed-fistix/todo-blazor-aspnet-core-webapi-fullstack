namespace Fistix.TaskManager.Core.Abstractions.Repositories
{
  public interface IRepositoryFactory
  {
    IUserProfileRepository UserProfileRepository { get; }
    ITodoTaskRepository TodoTaskRepository { get; }
  }
}