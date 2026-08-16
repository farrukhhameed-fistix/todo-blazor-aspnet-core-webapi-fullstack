using Fistix.TaskManager.ViewModel.Commands.Todos;
using Fistix.TaskManager.WebApp.Hubs;
using Fistix.TaskManager.WebApp.Models.Config;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;

namespace Fistix.TaskManager.WebApp.Services;

public sealed class VoiceTranscribeHubService : IAsyncDisposable
{
    private readonly IAccessTokenProvider _accessTokenProvider;
    private readonly ApiConfig _apiConfig;
    private readonly ILogger<VoiceTranscribeHubService> _logger;
    private readonly SemaphoreSlim _connectLock = new(1, 1);
    private HubConnection? _connection;
    private bool _sessionOpen;

    public VoiceTranscribeHubService(
        IAccessTokenProvider accessTokenProvider,
        ApiConfig apiConfig,
        ILogger<VoiceTranscribeHubService> logger)
    {
        _accessTokenProvider = accessTokenProvider;
        _apiConfig = apiConfig;
        _logger = logger;
    }

    public event Action<string>? PartialTranscript;

    public bool HasOpenSession => _sessionOpen;

    public Task WarmupAsync(CancellationToken cancellationToken = default) =>
        EnsureConnectedAsync(cancellationToken);

    public async Task<bool> StartSessionAsync(string contentType, string fileName, CancellationToken cancellationToken = default)
    {
        if (!await EnsureConnectedAsync(cancellationToken))
        {
            return false;
        }

        try
        {
            await _connection!.InvokeAsync("StartSession", contentType, fileName, cancellationToken);
            _sessionOpen = true;
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to start voice transcribe session");
            _sessionOpen = false;
            return false;
        }
    }

    public async Task AppendChunkAsync(string base64Chunk, CancellationToken cancellationToken = default)
    {
        if (!_sessionOpen || _connection?.State != HubConnectionState.Connected || string.IsNullOrWhiteSpace(base64Chunk))
        {
            return;
        }

        try
        {
            await _connection.InvokeAsync("AppendAudio", base64Chunk, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Voice chunk append failed");
        }
    }

    public async Task<TranscribeAudioResponseDto?> FinishSessionAsync(string? contextHint, CancellationToken cancellationToken = default)
    {
        if (!_sessionOpen)
        {
            return null;
        }

        _sessionOpen = false;
        if (_connection?.State != HubConnectionState.Connected)
        {
            return null;
        }

        try
        {
            return await _connection.InvokeAsync<TranscribeAudioResponseDto>("FinishSession", contextHint, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _logger.LogWarning(ex, "Voice hub finish failed; falling back to HTTP transcribe");
            return null;
        }
    }

    public async Task AbortSessionAsync(CancellationToken cancellationToken = default)
    {
        _sessionOpen = false;
        if (_connection?.State != HubConnectionState.Connected)
        {
            return;
        }

        try
        {
            await _connection.InvokeAsync("AbortSession", cancellationToken);
        }
        catch
        {
            // Best effort on cancel/dispose.
        }
    }

    private async Task<bool> EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (_connection is { State: HubConnectionState.Connected })
        {
            return true;
        }

        await _connectLock.WaitAsync(cancellationToken);
        try
        {
            if (_connection is { State: HubConnectionState.Connected })
            {
                return true;
            }

            if (_connection is not null)
            {
                await _connection.DisposeAsync();
            }

            var hubUrl = new Uri(new Uri(_apiConfig.Url), VoiceTranscribeHubClient.HubPath);
            _connection = new HubConnectionBuilder()
                .WithUrl(hubUrl, options =>
                {
                    options.AccessTokenProvider = async () =>
                    {
                        var tokenResult = await _accessTokenProvider.RequestAccessToken();
                        return tokenResult.TryGetToken(out var token) ? token.Value : null;
                    };
                })
                .WithAutomaticReconnect()
                .Build();

            _connection.On<string>(VoiceTranscribeHubClient.PartialTranscriptMethod, text =>
            {
                if (!string.IsNullOrWhiteSpace(text))
                {
                    PartialTranscript?.Invoke(text);
                }
            });

            await _connection.StartAsync(cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to connect to voice transcribe hub");
            return false;
        }
        finally
        {
            _connectLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await AbortSessionAsync();
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }

        _connectLock.Dispose();
    }
}
