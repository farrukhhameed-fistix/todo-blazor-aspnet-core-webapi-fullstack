using Fistix.TaskManager.WebApp.Models.Config;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Fistix.TaskManager.WebApp.Extentions
{
  public class CustomAuthorizationMessageHandler : AuthorizationMessageHandler
  {
    public CustomAuthorizationMessageHandler(IAccessTokenProvider provider,
        NavigationManager navigationManager, ApiConfig apiConfig)
        : base(provider, navigationManager)
    {
      ConfigureHandler(
          authorizedUrls: new[] { apiConfig.Url });

    }
  }
}
