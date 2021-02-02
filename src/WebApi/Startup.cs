using Fistix.TaskManager.Core.Config;
using Fistix.TaskManager.ServiceLayer;
using Fistix.TaskManager.ViewModel.Validators.Todos;
using Fistix.TaskManager.WebApi.Extensions;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Fistix.TaskManager.WebApi
{
  public class Startup
  {
    private readonly MasterConfig _masterConfig = new MasterConfig();

    public Startup(IConfiguration configuration)
    {
      Configuration = configuration;
      _masterConfig.PopulateConfiguration(Configuration);
    }

    public IConfiguration Configuration { get; }

    // This method gets called by the runtime. Use this method to add services to the container.
    public void ConfigureServices(IServiceCollection services)
    {

      services.AddControllers(options =>
      {
        //options.Filters.Add(typeof(Tracing.GlobalControllerAppInsightsAttribute));
      })
        .AddFluentValidation(x => x.RegisterValidatorsFromAssemblyContaining<CreateTodoTaskCommandValidator>());

      services.AddHttpContextAccessor();
      services.AddServiceLayer(_masterConfig);
      services.AddCommonServices(_masterConfig);
    }

    // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
      if (env.IsDevelopment())
      {
        app.UseDeveloperExceptionPage();        
      }

      app.UseHttpsRedirection();

      app.UseRouting();

      app.UseCommonService(_masterConfig);

      app.UseEndpoints(endpoints =>
      {
        endpoints.MapControllers();
      });
    }
  }
}
