using Fistix.TaskManager.Core.Abstractions.Services;
using Fistix.TaskManager.Core.Config;
using Fistix.TaskManager.Core.SecurityModel;
using Microsoft.AspNetCore.Builder;
using System.Linq;
using System.Security.Claims;

namespace Fistix.TaskManager.WebApi.Extensions
{
  public static class ApplicationBuilderExtension
  {
    public static void UseCommonService(this IApplicationBuilder app, MasterConfig masterConfig)
    {
      app.UseSwagger();

      app.UseSwaggerUI(x =>
      {
        x.SwaggerEndpoint($"/swagger/{masterConfig.Swagger.ApiVersion}/swagger.json", $"{masterConfig.Swagger.Title} {masterConfig.Swagger.ApiVersion}");
      });

      app.UseAuthentication();

      app.UseCors(masterConfig.AppConfig.DefaultCorsPolicyName);

      app.Use(async (context, next) =>
      {
        var userIdentity = context.User.Identities.FirstOrDefault();
        if (userIdentity != null)
        {
          ICurrentUserService currentUserService =
              (ICurrentUserService)context.RequestServices.GetService(typeof(ICurrentUserService));

          if (currentUserService.HasAdminProfile)
            userIdentity.AddClaim(new Claim(ClaimTypes.Role, RoleNames.Admin));
        }

        await next.Invoke();
      });

      app.UseAuthorization();
    }
  }
}