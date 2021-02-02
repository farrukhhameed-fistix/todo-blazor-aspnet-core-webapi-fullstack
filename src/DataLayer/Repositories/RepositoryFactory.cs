using Fistix.TaskManager.Core.Abstractions.Repositories;

namespace Fistix.TaskManager.DataLayer.Repositories
{
  public class RepositoryFactory : IRepositoryFactory
  {   
    private readonly EfContext context;
    private ITodoTaskRepository todoTaskRepository;
    private IUserProfileRepository userProfileRepository;

    public RepositoryFactory(EfContext context)
    {
      this.context = context;
    }

    public IUserProfileRepository UserProfileRepository
    {
      get { return userProfileRepository ??= new UserProfileRepository(context); }
    }
    public ITodoTaskRepository TodoTaskRepository
    {
      get { return todoTaskRepository ??= new TodoTaskRepository(context); }
    }
  }
}