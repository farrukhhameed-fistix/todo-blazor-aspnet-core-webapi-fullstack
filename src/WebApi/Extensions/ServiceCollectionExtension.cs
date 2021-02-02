using Fistix.TaskManager.WebApi.Services;
using Fistix.TaskManager.Core.Abstractions.Services;
using Fistix.TaskManager.Core.Config;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.Filters;
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Fistix.TaskManager.Core.SecurityModel;
using Fistix.TaskManager.WebApi.Filters;
using Microsoft.ApplicationInsights.Extensibility;
using Fistix.TaskManager.WebApi.Tracing;
using System.Reflection;
using System.IO;

namespace Fistix.TaskManager.WebApi.Extensions
{
  public static class ServiceCollectionExtension
  {
    public static void AddCommonServices(this IServiceCollection services, MasterConfig masterConfig)
    {
      string swaggerDescription = "";
      if (!string.IsNullOrEmpty(masterConfig.Auth0Config.AuthClientId))
      {
        swaggerDescription = $"Use ClientId: <b>{masterConfig.Auth0Config.AuthClientId}</b> to authorize<br /><u style='color: red;'>Be sure <b>openid profile email</b> has to be checked on authrization popup to get user Info from Auth0</u>";
      }

      services.AddSingleton<MasterConfig>(masterConfig);

      //services.AddSingleton<ITelemetryInitializer, TelemetryRequestResponse>();

      services.AddScoped<ICurrentUserService, CurrentUserService>();

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
            options.RequireHttpsMetadata = false;

            options.Events = new JwtBearerEvents
            {
              OnTokenValidated = context =>
                    {
                      if (!(context.SecurityToken is JwtSecurityToken token)) return Task.CompletedTask;
                      if (context.Principal.Identity is ClaimsIdentity identity)
                      {
                        identity.AddClaim(new Claim("access_token", token.RawData));
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