#nullable enable

using Fistix.TaskManager.AiLayer.Abstractions;
using Fistix.TaskManager.AiLayer.Agents;
using Fistix.TaskManager.AiLayer.Implementations;
using Fistix.TaskManager.AiLayer.Observability;
using Fistix.TaskManager.AiLayer.Shared;
using Fistix.TaskManager.AiLayer.Tools;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System;

namespace Fistix.TaskManager.WebApi.Extensions;

/// <summary>
/// Extension methods for registering AI services with dependency injection.
/// </summary>
public static class AiServiceExtension
{
    public static IServiceCollection AddAiServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(provider =>
        {
            var config = provider.GetRequiredService<IConfiguration>();
            var aiConfig = new AiConfiguration();
            config.GetSection("Ai").Bind(aiConfig);
            return aiConfig;
        });

        // Replace ServiceLayer's NullAiTelemetry TryAdd so WebApi uses real telemetry.
        services.RemoveAll<IAiTelemetry>();
        services.AddSingleton<IAiTelemetry, AiTelemetry>();
        services.AddSingleton<SemanticKernelOrchestrator>();

        services.AddSingleton(provider =>
        {
            var orchestrator = provider.GetRequiredService<SemanticKernelOrchestrator>();
            return orchestrator.CreateKernelAsync().GetAwaiter().GetResult();
        });

        services.AddSingleton<SemanticKernelLlmProvider>();
        services.AddSingleton<ILlmProviderService>(sp =>
            new ObservingLlmProvider(
                sp.GetRequiredService<SemanticKernelLlmProvider>(),
                sp.GetRequiredService<IAiTelemetry>(),
                sp.GetRequiredService<AiConfiguration>()));

        services.AddSingleton<AiChatClientFactory>();
        services.AddScoped<SummarizationPipeline>();
        services.AddScoped<ClassificationPipeline>();
        services.AddScoped<SemanticSearchPipeline>();
        services.AddScoped<RAGPipeline>();
        services.AddScoped<ToolProposalPipeline>();
        services.AddSingleton<TodoManagementPlugin>();
        services.AddHttpClient(nameof(SemanticKernelEmbeddingService));

        var embeddingProvider = configuration["Ai:Embedding:Provider"] ?? "Onnx";
        if (string.Equals(embeddingProvider, "onnx", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<OnnxBgeEmbeddingService>();
            services.AddSingleton<IEmbeddingService>(sp =>
                new ObservingEmbeddingService(
                    sp.GetRequiredService<OnnxBgeEmbeddingService>(),
                    sp.GetRequiredService<IAiTelemetry>()));
        }
        else
        {
            services.AddScoped<SemanticKernelEmbeddingService>();
            services.AddScoped<IEmbeddingService>(sp =>
                new ObservingEmbeddingService(
                    sp.GetRequiredService<SemanticKernelEmbeddingService>(),
                    sp.GetRequiredService<IAiTelemetry>()));
        }

        return services;
    }
}
