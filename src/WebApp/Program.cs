using Fistix.TaskManager.ViewModel.Validators.Todos;
using Fistix.TaskManager.WebApp.Extentions;
using Fistix.TaskManager.WebApp.Services;
using Fistix.TaskManager.WebApp.Services.DataServices;
using Fistix.TaskManager.WebApp.Services.StateServices;
using Fistix.TaskManager.WebApp.Shared.Theme;
using FluentValidation;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MudBlazor.Services;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace Fistix.TaskManager.WebApp
{
  public class Program
  {
    public static async Task Main(string[] args)
    {
      var builder = WebAssemblyHostBuilder.CreateDefault(args);
      builder.RootComponents.Add<App>("#app");

      // AuthorizeView logs anonymous checks at Information; that is expected during login transitions.
      builder.Logging.AddFilter("Microsoft.AspNetCore.Authorization", LogLevel.Warning);

      ConfigureServices(builder.Services, builder.Configuration, builder.HostEnvironment.BaseAddress);

      await builder.Build().RunAsync();
    }

    public static void ConfigureServices(
      IServiceCollection services,
      IConfigurationRoot configuration,
      string baseAddress)
    {
      services.AddValidatorsFromAssemblyContaining<CreateTodoTaskCommandValidator>();
      services.AddMudServices();

      services.SetupAuth0Service(configuration, baseAddress);
      services.SetupDefaultApiClient(configuration);
      //services.AddScoped(sp => new HttpClient { BaseAddress = new Uri("https://localhost:5001") });
      
      services.AddScoped<TodoDataService>();
      services.AddScoped<TodoStateService>();
      services.AddScoped<ClassificationHubService>();
      services.AddScoped<AiBatchHubService>();
      services.AddScoped<SprintOptimizerHubService>();
      services.AddScoped<VoiceTranscribeHubService>();
      services.AddScoped<KnowledgeDataService>();
      services.AddScoped<KnowledgeIngestHubService>();
      services.AddScoped<ThemeService>();
    }
  }
}
