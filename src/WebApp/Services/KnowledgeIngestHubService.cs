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

public sealed class KnowledgeIngestHubService : IAsyncDisposable
{
    private readonly IAccessTokenProvider _accessTokenProvider;
    private readonly ApiConfig _apiConfig;
    private readonly ILogger<KnowledgeIngestHubService> _logger;
    private HubConnection? _connection;
    private readonly SemaphoreSlim _connectLock = new(1, 1);
    private Guid? _joinedJobId;

    public KnowledgeIngestHubService(
        IAccessTokenProvider accessTokenProvider,
        ApiConfig apiConfig,
        ILogger<KnowledgeIngestHubService> logger)
    {
        _accessTokenProvider = accessTokenProvider;
        _apiConfig = apiConfig;
        _logger = logger;
    }

    public event Action<KnowledgeIngestJobDto>? JobUpdated;

    public async Task SubscribeToJobAsync(Guid jobExternalId, CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(cancellationToken);

        if (_joinedJobId.HasValue && _joinedJobId.Value != jobExternalId)
        {
            await LeaveJobAsync(_joinedJobId.Value, cancellationToken);
        }

        if (_connection?.State == HubConnectionState.Connected)
        {
            await _connection.InvokeAsync("JoinJob", jobExternalId, cancellationToken);
            _joinedJobId = jobExternalId;
        }
    }

    public async Task LeaveJobAsync(Guid jobExternalId, CancellationToken cancellationToken = default)
    {
        if (_connection?.State == HubConnectionState.Connected)
        {
            await _connection.InvokeAsync("LeaveJob", jobExternalId, cancellationToken);
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

            var hubUrl = new Uri(new Uri(_apiConfig.Url), KnowledgeIngestHubClient.HubPath);
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

            _connection.On<KnowledgeIngestJobDto>(KnowledgeIngestHubClient.JobUpdatedMethod, dto =>
            {
                JobUpdated?.Invoke(dto);
            });

            await _connection.StartAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to connect to knowledge ingest hub");
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
