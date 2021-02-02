using Fistix.TaskManager.WebApp.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;

namespace Fistix.TaskManager.WebApp.Extentions
{
  public static class HttpResponseMessageExtention
  {
    public static async Task<string> GetErrorMessage(this HttpResponseMessage response)
    {
      string errorMessage = string.Empty;

      if (response.StatusCode == System.Net.HttpStatusCode.BadRequest ||
          response.StatusCode == System.Net.HttpStatusCode.Conflict ||
          response.StatusCode == System.Net.HttpStatusCode.NotFound)
      {
        var problemDetails = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        if (!String.IsNullOrWhiteSpace(problemDetails.Detail))
        {
          errorMessage = problemDetails.Detail;
        }
        else if (problemDetails.Errors != null && problemDetails.Errors.Any())
        {
          var errors = problemDetails.Errors.Select(x => x.Value.Count > 1 ? String.Join(',', x.Value) : x.Value.First()).ToList();
          if (errors != null && errors.Any())
            errorMessage = string.Join(',', errors);
        }
        else
        {
          errorMessage = problemDetails.Title;
        }
      }
      else 
      {
        errorMessage = await response.Content.ReadAsStringAsync();
      }

      return errorMessage;
    }
  }
}
