using Fistix.TaskManager.Core.Abstractions.Repositories;
using Fistix.TaskManager.Core.Config;
using Fistix.TaskManager.DataLayer.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Fistix.TaskManager.DataLayer
{
  public static class Startup
  {
    public static void AddDataLayer(this IServiceCollection services, MasterConfig masterConfig)
    {
      services.AddDbContext<EfContext>(options => options.UseSqlServer(masterConfig.ConnectionString.MainDb));
      services.AddScoped<ITodoTaskRepository, TodoTaskRepository>();
      services.AddScoped<IUserProfileRepository, UserProfileRepository>();
      services.AddScoped<IRepositoryFactory, RepositoryFactory>();
    }
  }
}
