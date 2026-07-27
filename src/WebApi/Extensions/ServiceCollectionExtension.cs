using Fistix.TaskManager.WebApi.Services;
using Fistix.TaskManager.Core.Abstractions.Services;
using Fistix.TaskManager.Core.Config;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.Filters;
using System;
using System.Collections.Generic;
using System.Linq;
using Fistix.TaskManager.Core.SecurityModel;
using Fistix.TaskManager.WebApi.Filters;
using System.Reflection;
using System.IO;
using System.Security.Claims;
using System.Threading.Tasks;

namespace Fistix.TaskManager.WebApi.Extensions
{
  public static class ServiceCollectionExtension
  {
    public static void AddCommonServices(this IServiceCollection services, MasterConfig masterConfig, bool isDevelopment)
    {
      string swaggerDescription = "";
      if (!string.IsNullOrEmpty(masterConfig.Auth0Config.AuthClientId))
      {
        swaggerDescription = $"Use ClientId: <b>{masterConfig.Auth0Config.AuthClientId}</b> to authorize<br /><u style='color: red;'>Be sure <b>openid profile email</b> has to be checked on authrization popup to get user Info from Auth0</u>";
      }

      services.AddSingleton<MasterConfig>(masterConfig);

      //services.AddSingleton<ITelemetryInitializer, TelemetryRequestResponse>();

      services.AddScoped<ICurrentUserService, CurrentUserService>();
      services.AddScoped<IAccessTokenProvider, AccessTokenProvider>();

      services.AddCors(options =>
      {
        options.AddPolicy(masterConfig.AppConfig.DefaultCorsPolicyName, builder =>
              {
                builder
                          .WithOrigins(
                              masterConfig.AppConfig.CorsOrigins.Split(',').ToArray()
                          )
                          .SetIsOriginAllowedToAllowWildcardSubdomains()
                          .AllowAnyHeader()
                          .AllowAnyMethod()
                          .AllowCredentials();
              });
      });

      services.AddSwaggerGen(c =>
      {
        c.SwaggerDoc(masterConfig.Swagger.ApiVersion, new OpenApiInfo { Title = masterConfig.Swagger.Title, Version = masterConfig.Swagger.ApiVersion, Description = swaggerDescription });

        c.AddSecurityDefinition("oauth2", new OpenApiSecurityScheme
        {
          Type = SecuritySchemeType.OAuth2,
          Flows = new OpenApiOAuthFlows
          {
            Implicit = new OpenApiOAuthFlow
            {
              AuthorizationUrl = new Uri($"{masterConfig.Auth0Config.Authority}authorize?audience={masterConfig.Auth0Config.Audience}"),
              Scopes = new Dictionary<string, string>()
                      {
                                {"openid profile email", "Get all required info from Auth0" }
                      }
            }
          }
        });

        c.OperationFilter<SecurityRequirementsOperationFilter>();
        c.OperationFilter<SwaggerJsonIgnorFilter>();

        var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
        var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);

        c.IncludeXmlComments(xmlPath);
      });

      services
          .AddAuthentication(options =>
          {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultSignInScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;

          })
          .AddJwtBearer(options =>
          {
            options.Authority = masterConfig.Auth0Config.Authority;
            options.Audience = masterConfig.Auth0Config.Audience;
            options.RequireHttpsMetadata = !isDevelopment;
            options.SaveToken = true;
            options.Events = new JwtBearerEvents
            {
              OnMessageReceived = context =>
              {
                // SignalR WebSockets pass the JWT as ?access_token= (not Authorization header).
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                {
                  context.Token = accessToken;
                }

                return Task.CompletedTask;
              }
            };
          });

      services.AddAuthorization(options =>
      {
        options.AddPolicy(PolicyNames.IsAdmin,
            policy => policy.RequireClaim(ClaimTypes.Role, RoleNames.Admin))
          ;
      });
    }
  }
}