using Fistix.TaskManager.WebApp.Models.Config;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Fistix.TaskManager.WebApp.Extentions
{
  public static class ServiceCollectionExtentions
  {
    public static void SetupAuth0Service(this IServiceCollection services, IConfigurationRoot configurationRoot)
    {
      Auth0Config auth0Config = configurationRoot.GetSection("Auth0").Get<Auth0Config>();

      services.AddSingleton(auth0Config);

      services.AddOidcAuthentication(options =>
      {
        options.ProviderOptions.Authority = auth0Config.Authority;
        options.ProviderOptions.ClientId = auth0Config.ClientId;
        options.ProviderOptions.ResponseType = "id_token token";
        options.ProviderOptions.DefaultScopes.Add(auth0Config.Scope);
      });
    }

    public static void SetupDefaultApiClient(this IServiceCollection services, IConfigurationRoot configurationRoot)
    {
      var apiConfig = configurationRoot.GetSection("Api").Get<ApiConfig>();
      services.AddSingleton(apiConfig);

      services.AddScoped<CustomAuthorizationMessageHandler>();

      services.AddHttpClient("defaultApi", client => client.BaseAddress = new Uri(apiConfig.Url))
          .AddHttpMessageHandler<CustomAuthorizationMessageHandler>();

      services.AddScoped(sp => sp.GetRequiredService<System.Net.Http.IHttpClientFactory>().CreateClient("defaultApi"));
    }


  }
}
