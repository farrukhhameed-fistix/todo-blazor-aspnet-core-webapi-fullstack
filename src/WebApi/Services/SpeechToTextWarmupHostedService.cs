using System;
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
        _ = Task.Run(() => RetryWarmupAsync(cancellationToken), cancellationToken);
        return Task.CompletedTask;
    }

    /// <summary>
    /// No-op stop hook.
    /// </summary>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task RetryWarmupAsync(CancellationToken cancellationToken)
    {
        const int maxAttempts = 8;
        var delay = TimeSpan.FromSeconds(2);

        for (var attempt = 1; attempt <= maxAttempts && !cancellationToken.IsCancellationRequested; attempt++)
        {
            _warmup.EnsureModelInBackground();

            // Give background warmup a moment to start and update state.
            await Task.Delay(TimeSpan.FromMilliseconds(350), cancellationToken);

            if (_warmup.IsReady)
            {
                _logger.LogInformation("Speech model warmup ready on attempt {Attempt}", attempt);
                return;
            }

            if (attempt == maxAttempts)
            {
                _logger.LogWarning(
                    "Speech model warmup did not become ready after {Attempts} attempts. Last error: {LastError}",
                    maxAttempts,
                    _warmup.LastError ?? "unknown");
                return;
            }

            if (!string.IsNullOrWhiteSpace(_warmup.LastError))
            {
                _logger.LogInformation(
                    "Speech warmup attempt {Attempt}/{MaxAttempts} not ready yet: {LastError}",
                    attempt,
                    maxAttempts,
                    _warmup.LastError);
            }

            await Task.Delay(delay, cancellationToken);
            delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 1.5, 10));
        }
    }
}
