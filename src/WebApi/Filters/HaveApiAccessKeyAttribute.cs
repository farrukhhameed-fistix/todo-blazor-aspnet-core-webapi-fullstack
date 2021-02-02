using Fistix.TaskManager.Core.Config;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;

namespace Fistix.TaskManager.WebApi.Filters
{
  [AttributeUsage(validOn: AttributeTargets.Class | AttributeTargets.Method)]
  public class HaveApiAccessKeyAttribute : Attribute, IAsyncActionFilter
  {
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
      var masterConfig = context.HttpContext.RequestServices.GetRequiredService<MasterConfig>();

      if (context.HttpContext.Request.Headers.TryGetValue("ApiAccessKey", out var extractedApiKey)
          && masterConfig.AppConfig.ApiAccessKey.Equals(extractedApiKey))
      {
        await next();
      }

      context.Result = new UnauthorizedResult();
      return;
    }
  }
}