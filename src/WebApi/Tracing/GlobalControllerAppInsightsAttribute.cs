using Microsoft.AspNetCore.Mvc.Filters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Fistix.TaskManager.WebApi.Tracing
{
  public class GlobalControllerAppInsightsAttribute : ActionFilterAttribute
  {
    public override void OnActionExecuting(ActionExecutingContext context)
    {
      GlobalStoredTraces.AddRequestTrace(context.HttpContext);
      base.OnActionExecuting(context);
    }

    public override void OnResultExecuted(ResultExecutedContext context)
    {
      GlobalStoredTraces.AddResponseTrace(context);
      base.OnResultExecuted(context);
    }
  }
}
