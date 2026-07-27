using Azure.Extensions.AspNetCore.Configuration;
using Azure.Identity;
using Fistix.TaskManager.Core.Config;
using Fistix.TaskManager.DataLayer;
using Fistix.TaskManager.ServiceLayer;
using Fistix.TaskManager.ViewModel.Validators.Todos;
using Fistix.TaskManager.WebApi.Extensions;
using Fistix.TaskManager.WebApi.Hubs;
using FluentValidation.AspNetCore;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using System;
using System.Collections.Generic;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

var keyVaultUri = builder.Configuration["KeyVault:Uri"];

if (builder.Environment.IsProduction() && !string.IsNullOrWhiteSpace(keyVaultUri))
{
    builder.Configuration.AddAzureKeyVault(new Uri(keyVaultUri), new DefaultAzureCredential());
}

// Azure App Configuration (if available)
var appConfigConnectionString = Environment.GetEnvironmentVariable("AppConfigConnectionString");
if (!string.IsNullOrWhiteSpace(appConfigConnectionString))
{
    builder.Configuration.AddAzureAppConfiguration(options =>
    {
        options
            .Connect(appConfigConnectionString)
            .Select(KeyFilter.Any, null)
            .Select(KeyFilter.Any, Environment.GetEnvironmentVariable("AppConfigEnvironmentName"));
    });
}

// Load configuration and populate MasterConfig
var masterConfig = new MasterConfig();
masterConfig.PopulateConfiguration(builder.Configuration);

// Add services to the container
builder.Services.AddControllers(options =>
{
    //options.Filters.Add(typeof(Tracing.GlobalControllerAppInsightsAttribute));
})
.AddFluentValidation(x => x.RegisterValidatorsFromAssemblyContaining<CreateTodoTaskCommandValidator>());

builder.Services.AddHttpContextAccessor();
builder.Services.AddServiceLayer(masterConfig);
builder.Services.AddCommonServices(masterConfig, builder.Environment.IsDevelopment());
builder.Services.AddAiServices(builder.Configuration);
builder.Services.AddClassificationSignalR();
builder.Services.AddAiRateLimiting(builder.Configuration);

var app = builder.Build();

app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    Fistix.TaskManager.DataLayer.Startup.ApplyMigrations(app.Services);
}

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseSecurityHeaders();
app.UseRouting();
app.UseCommonService(masterConfig, app.Environment);

app.MapControllers();
app.MapHub<ClassificationHub>(ClassificationHub.HubPath);
app.MapHub<AiBatchHub>(AiBatchHub.HubPath);
app.MapHub<SprintOptimizerHub>(SprintOptimizerHub.HubPath);

await app.RunAsync();
