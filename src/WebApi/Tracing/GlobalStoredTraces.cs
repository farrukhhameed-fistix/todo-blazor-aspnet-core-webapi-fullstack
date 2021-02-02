using System.Collections.Concurrent;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Newtonsoft.Json;

namespace Fistix.TaskManager.WebApi.Tracing
{
  public class GlobalStoredTraces
  {
    public static readonly ConcurrentDictionary<string, GlobalStoredTraces> CurrentTraces = new ConcurrentDictionary<string, GlobalStoredTraces>();

    // Content to keep (and remove when sent).
    public string Id { get; private set; }
    public string Body { get; set; }
    public bool HasBody => !string.IsNullOrEmpty(Body);

    // Static methods to manage this ConcurrentDictionary.
    public static readonly GlobalStoredTraces Empty = new GlobalStoredTraces();

    public static GlobalStoredTraces AddRequestTrace(HttpContext context)
    {
      var trace = new GlobalStoredTraces()
      {
        Id = context.TraceIdentifier,
        Body = GetHttpRequest(context),
      };
      CurrentTraces.TryAdd($"Request_{trace.Id}", trace);
      return trace;
    }

    public static GlobalStoredTraces GetRequestTrace(string id)
    {
      return CurrentTraces.TryRemove($"Request_{id}", out var trace) ? trace : Empty;
    }

    public static GlobalStoredTraces AddResponseTrace(ResultExecutedContext context)
    {
      var trace = new GlobalStoredTraces()
      {
        Id = context.HttpContext.TraceIdentifier,
        Body = GetHttpResult(context.Result),
      };
      CurrentTraces.TryAdd($"Response_{trace.Id}", trace);
      return trace;
    }

    public static GlobalStoredTraces GetResponseTrace(string id)
    {
      return CurrentTraces.TryRemove($"Response_{id}", out var trace) ? trace : Empty;
    }

    private static string GetHttpRequest(HttpContext context)
    {
      string body;

      if (context?.Request?.Body == null) return string.Empty;
      if (!context.Request.Body.CanRead) return string.Empty;
      if (!context.Request.Body.CanSeek) return string.Empty;

      context.Request.Body.Position = 0;
      using var reader = new System.IO.StreamReader(context.Request.Body);
      body = reader.ReadToEnd();

      return body;
    }

    private static string GetHttpResult(IActionResult result)
    {
      if (result is ObjectResult objectResult)
        return JsonConvert.SerializeObject(objectResult.Value);
      else
        return JsonConvert.SerializeObject(result);
    }
  }
}
