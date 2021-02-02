using Microsoft.OpenApi.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace Fistix.TaskManager.WebApi.Filters
{
  public class SwaggerJsonIgnorFilter : Swashbuckle.AspNetCore.SwaggerGen.IOperationFilter
  {
    public void Apply(OpenApiOperation operation, Swashbuckle.AspNetCore.SwaggerGen.OperationFilterContext context)
    {

      var ignoredProperties = context.MethodInfo.GetParameters()
          .SelectMany(p => p.ParameterType.GetProperties()
                           .Where(prop => prop.GetCustomAttribute<JsonIgnoreAttribute>() != null)
                           );
      if (ignoredProperties.Any())
      {
        foreach (var property in ignoredProperties)
        {
          operation.Parameters = operation.Parameters
              .Where(p => !p.Name.Equals(property.Name, StringComparison.InvariantCulture))
              .ToList();
        }

      }
    }
  }
}
