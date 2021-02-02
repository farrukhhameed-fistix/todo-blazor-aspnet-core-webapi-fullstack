using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.AspNetCore.Http;

namespace Fistix.TaskManager.WebApi.Tracing
{
  public class TelemetryRequestResponse : ITelemetryInitializer
  {
    private readonly IHttpContextAccessor _httpContextAccessor;

    public TelemetryRequestResponse(IHttpContextAccessor httpContextAccessor)
    {
      _httpContextAccessor = httpContextAccessor;
    }

    public void Initialize(ITelemetry telemetry)
    {
      if (!(telemetry is RequestTelemetry requestTelemetry)) return;

      var id = _httpContextAccessor.HttpContext.TraceIdentifier;
      var request = GlobalStoredTraces.GetRequestTrace(id);
      var response = GlobalStoredTraces.GetResponseTrace(id);

      requestTelemetry.Properties.Add("requestBody", request.Body);
      requestTelemetry.Properties.Add("responseBody", response.Body);
    }
  }
}
