using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Fistix.TaskManager.WebApi
{
  public class Program
  {
    public static void Main(string[] args)
    {
      CreateHostBuilder(args).Build().Run();
    }

    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureWebHostDefaults(webBuilder =>
            {
              var appConfigConnectionString = Environment.GetEnvironmentVariable("AppConfigConnectionString");
              if (!string.IsNullOrWhiteSpace(appConfigConnectionString))
              {
                webBuilder.ConfigureAppConfiguration((hostingContext, config) =>
                {
                  var settings = config.Build();
                  config.AddAzureAppConfiguration(options =>
                  {
                    options
                            .Connect(appConfigConnectionString)
                            .Select(KeyFilter.Any, null)
                            .Select(KeyFilter.Any,
                                Environment.GetEnvironmentVariable("AppConfigEnvironmentName"));
                  });
                });
              }
              webBuilder.UseStartup<Startup>();
            });
  }
}
