#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using Fistix.TaskManager.ViewModel.Dtos;
using Fistix.TaskManager.WebApp.Hubs;
using Fistix.TaskManager.WebApp.Models.Config;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;

namespace Fistix.TaskManager.WebApp.Services;

public sealed class SprintOptimizerHubService : IAsyncDisposable
{
    private readonly IAccessTokenProvider _accessTokenProvider;
    private readonly ApiConfig _apiConfig;
    private readonly ILogger<SprintOptimizerHubService> _logger;
    private HubConnection? _connection;
    private readonly SemaphoreSlim _connectLock = new(1, 1);
    private Guid? _joinedJobId;

    public SprintOptimizerHubService(
        IAccessTokenProvider accessTokenProvider,
        ApiConfig apiConfig,
        ILogger<SprintOptimizerHubService> logger)
    {
        _accessTokenProvider = accessTokenProvider;
        _apiConfig = apiConfig;
        _logger = logger;
    }

    public event Action<SprintOptimizerJobDto>? JobUpdated;

    /// <summary>Raised after automatic reconnect + re-JoinJob so UI can refresh status via HTTP.</summary>
    public event Func<Guid, Task>? ReconnectedAsync;

    /// <summary>Raised when the hub connection drops (before reconnect attempts finish).</summary>
    public event Action? ConnectionLost;

    public bool IsConnected => _connection?.State == HubConnectionState.Connected;

    public async Task SubscribeToJobAsync(Guid jobExternalId, CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);

        if (_joinedJobId.HasValue && _joinedJobId.Value != jobExternalId)
        {
            await LeaveJobAsync(_joinedJobId.Value, cancellationToken);
        }

        _joinedJobId = jobExternalId;

        if (_connection?.State == HubConnectionState.Connected)
        {
            await _connection.InvokeAsync("JoinJob", jobExternalId, cancellationToken);
        }
    }

    public async Task LeaveJobAsync(Guid jobExternalId, CancellationToken cancellationToken = default)
    {
        if (_connection?.State == HubConnectionState.Connected)
        {
            try
            {
                await _connection.InvokeAsync("LeaveJob", jobExternalId, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "LeaveJob failed for sprint optimizer hub");
            }
        }

        if (_joinedJobId == jobExternalId)
        {
            _joinedJobId = null;
        }
    }

    private async Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (_connection is { State: HubConnectionState.Connected })
        {
            return;
        }

        await _connectLock.WaitAsync(cancellationToken);
        try
        {
            if (_connection is { State: HubConnectionState.Connected })
            {
                return;
            }

            if (_connection is not null)
            {
                await _connection.DisposeAsync();
            }

            var hubUrl = new Uri(new Uri(_apiConfig.Url), SprintOptimizerHubClient.HubPath);
            _connection = new HubConnectionBuilder()
                .WithUrl(hubUrl, options =>
                {
                    options.AccessTokenProvider = async () =>
                    {
                        var tokenResult = await _accessTokenProvider.RequestAccessToken();
                        return tokenResult.TryGetToken(out var token) ? token.Value : null;
                    };
                })
                .WithAutomaticReconnect(new[]
                {
                    TimeSpan.Zero,
                    TimeSpan.FromSeconds(2),
                    TimeSpan.FromSeconds(5),
                    TimeSpan.FromSeconds(10),
                    TimeSpan.FromSeconds(30)
                })
                .Build();

            _connection.On<SprintOptimizerJobDto>(SprintOptimizerHubClient.JobUpdatedMethod, dto =>
            {
                JobUpdated?.Invoke(dto);
            });

            _connection.Reconnecting += error =>
            {
                _logger.LogWarning(error, "Sprint optimizer hub reconnecting");
                ConnectionLost?.Invoke();
                return Task.CompletedTask;
            };

            _connection.Reconnected += async connectionId =>
            {
                _logger.LogInformation("Sprint optimizer hub reconnected ({ConnectionId})", connectionId);
                var jobId = _joinedJobId;
                if (jobId.HasValue && _connection is { State: HubConnectionState.Connected })
                {
                    try
                    {
                        await _connection.InvokeAsync("JoinJob", jobId.Value);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to re-join sprint optimizer job after reconnect");
                    }

                    var handler = ReconnectedAsync;
                    if (handler is not null)
                    {
                        await handler(jobId.Value);
                    }
                }
            };

            _connection.Closed += error =>
            {
                _logger.LogWarning(error, "Sprint optimizer hub closed");
                ConnectionLost?.Invoke();
                return Task.CompletedTask;
            };

            await _connection.StartAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to connect to sprint optimizer hub");
        }
        finally
        {
            _connectLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_joinedJobId.HasValue && _connection is { State: HubConnectionState.Connected })
        {
            try
            {
                await _connection.InvokeAsync("LeaveJob", _joinedJobId.Value);
            }
            catch
            {
                // best effort
            }
        }

        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }

        _connectLock.Dispose();
    }
}
