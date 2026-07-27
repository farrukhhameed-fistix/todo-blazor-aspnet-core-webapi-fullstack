using Fistix.TaskManager.AiLayer.Abstractions;
using Fistix.TaskManager.Core.AutoMapperProfiles;
using Fistix.TaskManager.Core.Config;
using Fistix.TaskManager.DataLayer;
using Fistix.TaskManager.ServiceLayer.Background;
using Fistix.TaskManager.ServiceLayer.Notifications;
using Fistix.TaskManager.ServiceLayer.Todos;
using Fistix.TaskManager.ViewModel.Commands.Todos;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Fistix.TaskManager.ServiceLayer
{
  public static class StartUp
  {
    public static void AddServiceLayer(this IServiceCollection services, MasterConfig masterConfig)
    {
      services.AddMediatR(typeof(CreateTodoTaskCommand).Assembly, typeof(CreateTodoTaskCommandHandler).Assembly);

      services.AddAutoMapper(x=>x.AddProfile<TodoTaskProfileMapping>());

        services.AddScoped<IToolExecutor, ToolExecutor>();
      services.AddScoped<SprintPlanningTools>();
      services.AddScoped<SprintOptimizerAgent>();

      services.AddSingleton<IClassificationQueue, ClassificationQueue>();
      services.AddScoped<IClassificationProcessor, ClassificationProcessor>();
      services.AddHostedService<ClassificationBackgroundService>();

      services.AddSingleton<IEmbeddingQueue, EmbeddingQueue>();
      services.AddScoped<IEmbeddingProcessor, EmbeddingProcessor>();
      services.AddScoped<IVectorStore, PgVectorEmbeddingStore>();
      services.AddHostedService<EmbeddingBackgroundService>();

      services.AddScoped<IAiBatchStepExecutor, AiBatchStepExecutor>();
      services.AddHostedService<AiBatchBackgroundService>();
      // SignalR notifier is registered by WebApi; tests register NullAiBatchNotifier.
      services.TryAddSingleton<IAiBatchNotifier, NullAiBatchNotifier>();
            
      services.AddDataLayer(masterConfig);
    }
  }
}
