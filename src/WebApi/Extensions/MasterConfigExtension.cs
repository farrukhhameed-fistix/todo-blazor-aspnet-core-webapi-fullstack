using Fistix.TaskManager.Core.Config;
using Microsoft.Extensions.Configuration;

namespace Fistix.TaskManager.WebApi.Extensions
{
  public static class MasterConfigExtension
  {
    public static void PopulateConfiguration(this MasterConfig masterConfig, IConfiguration configuration)
    {
      masterConfig.AppConfig = configuration.GetSection("App").Get<AppConfig>();
      masterConfig.Auth0Config = configuration.GetSection("Auth0").Get<Auth0Config>();
      masterConfig.ConnectionString = configuration.GetSection("ConnectionStrings").Get<ConnectionStringConfig>();
      masterConfig.Swagger = configuration.GetSection("Swagger").Get<SwaggerConfig>();
      masterConfig.AzureConfig = configuration.GetSection("Azure").Get<AzureConfig>();
    }
  }
}