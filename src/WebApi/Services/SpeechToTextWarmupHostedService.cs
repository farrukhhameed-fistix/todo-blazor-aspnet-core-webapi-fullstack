using System.Threading;
using System.Threading.Tasks;
using Fistix.TaskManager.AiLayer.Abstractions;
using Fistix.TaskManager.AiLayer.Shared;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Fistix.TaskManager.WebApi.Services;

/// <summary>
/// Starts non-blocking speech model warmup during API startup.
/// </summary>
public sealed class SpeechToTextWarmupHostedService : IHostedService
{
    private readonly AiConfiguration _aiConfig;
    private readonly ISpeechToTextModelWarmup _warmup;
    private readonly ILogger<SpeechToTextWarmupHostedService> _logger;

    /// <summary>
    /// Initializes a new warmup hosted service.
    /// </summary>
    public SpeechToTextWarmupHostedService(
        AiConfiguration aiConfig,
        ISpeechToTextModelWarmup warmup,
        ILogger<SpeechToTextWarmupHostedService> logger)
    {
        _aiConfig = aiConfig;
        _warmup = warmup;
        _logger = logger;
    }

    /// <summary>
    /// Triggers model warmup in background and returns immediately.
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_aiConfig.Features.EnableVoiceTranscription)
        {
            return Task.CompletedTask;
        }

        _logger.LogInformation("Starting non-blocking speech model warmup");
        _warmup.EnsureModelInBackground();
        return Task.CompletedTask;
    }

    /// <summary>
    /// No-op stop hook.
    /// </summary>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
