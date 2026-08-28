using System;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Fistix.TaskManager.AiLayer.Shared;
using Fistix.TaskManager.Core.SecurityModel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Fistix.TaskManager.WebApi.Extensions;

public static class RateLimitingServiceExtension
{
    public static IServiceCollection AddAiRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var aiConfig = new AiConfiguration();
        configuration.GetSection("Ai").Bind(aiConfig);

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, cancellationToken) =>
            {
                var httpContext = context.HttpContext;
                httpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;

                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    httpContext.Response.Headers.RetryAfter =
                        ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();
                }

                await httpContext.Response.WriteAsJsonAsync(new ProblemDetails
                {
                    Title = "Too many requests",
                    Detail = "AI rate limit exceeded. Please try again later.",
                    Status = StatusCodes.Status429TooManyRequests
                }, cancellationToken);
            };

            AddFixedWindowPolicy(options, RateLimitPolicies.AiSummarize, aiConfig.Features.SummarizeRateLimit);
            AddFixedWindowPolicy(options, RateLimitPolicies.AiClassify, aiConfig.Features.ClassifyRateLimit);
            AddFixedWindowPolicy(options, RateLimitPolicies.AiSemanticSearch, aiConfig.Features.SemanticSearchRateLimit);
            AddFixedWindowPolicy(options, RateLimitPolicies.AiRag, aiConfig.Features.RagRateLimit);
            AddFixedWindowPolicy(options, RateLimitPolicies.AiKnowledgeRag, aiConfig.Features.KnowledgeRagRateLimit);
            AddFixedWindowPolicy(options, RateLimitPolicies.AiFunctionCalling, aiConfig.Features.FunctionCallingRateLimit);
            AddFixedWindowPolicy(options, RateLimitPolicies.AiAgents, aiConfig.Features.AgentsRateLimit);
            AddFixedWindowPolicy(options, RateLimitPolicies.AiTranscribe, aiConfig.Features.VoiceTranscriptionRateLimit);
        });

        return services;
    }

    private static void AddFixedWindowPolicy(
        RateLimiterOptions options,
        string policyName,
        AiRateLimitConfiguration rateLimit)
    {
        if (!rateLimit.Enabled)
        {
            options.AddPolicy(policyName, _ =>
                RateLimitPartition.GetNoLimiter(policyName));
            return;
        }

        var permitLimit = Math.Max(1, rateLimit.PermitLimit);
        var window = TimeSpan.FromMinutes(Math.Max(1, rateLimit.WindowMinutes));

        options.AddPolicy(policyName, httpContext =>
        {
            var partitionKey = httpContext.User?.FindFirstValue("sub")
                ?? httpContext.User?.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? httpContext.Connection.RemoteIpAddress?.ToString()
                ?? "anonymous";

            return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = window,
                QueueLimit = 0,
                AutoReplenishment = true
            });
        });
    }
}
