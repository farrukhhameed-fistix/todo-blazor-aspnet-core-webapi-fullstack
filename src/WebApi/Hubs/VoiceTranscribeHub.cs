#nullable enable

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Fistix.TaskManager.AiLayer.Shared;
using Fistix.TaskManager.Core.Exceptions;
using Fistix.TaskManager.ViewModel.Commands.Todos;
using Fistix.TaskManager.ViewModel.Validators.Todos;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace Fistix.TaskManager.WebApi.Hubs;

[Authorize]
public sealed class VoiceTranscribeHub : Hub
{
    public const string HubPath = "/hubs/voice-transcribe";
    public const string PartialTranscriptMethod = "PartialTranscript";

    private static readonly ConcurrentDictionary<string, Session> Sessions = new();
    private static readonly TimeSpan PartialMinInterval = TimeSpan.FromSeconds(2.5);
    private static readonly TimeSpan PcmPartialMinInterval = TimeSpan.FromSeconds(2.5);
    private const int PartialMinBytes = 20 * 1024;

    private readonly IMediator _mediator;
    private readonly IValidator<TranscribeAudioCommand> _validator;
    private readonly AiConfiguration _aiConfig;
    private readonly ILogger<VoiceTranscribeHub> _logger;

    public VoiceTranscribeHub(
        IMediator mediator,
        IValidator<TranscribeAudioCommand> validator,
        AiConfiguration aiConfig,
        ILogger<VoiceTranscribeHub> logger)
    {
        _mediator = mediator;
        _validator = validator;
        _aiConfig = aiConfig;
        _logger = logger;
    }

    public Task StartSession(string? contentType, string? fileName)
    {
        var mediaType = string.IsNullOrWhiteSpace(contentType) ? "audio/webm" : contentType.Split(';', 2)[0].Trim();
        var pcm = PcmWavWriter.IsRawPcm(mediaType);
        var session = new Session
        {
            ContentType = mediaType,
            FileName = string.IsNullOrWhiteSpace(fileName)
                ? (pcm ? "recording.pcm" : "recording.webm")
                : fileName,
            SampleRate = _aiConfig.SpeechToText.PcmSampleRate > 0
                ? _aiConfig.SpeechToText.PcmSampleRate
                : 16000
        };

        if (Sessions.TryRemove(Context.ConnectionId, out var previous))
        {
            previous.Dispose();
        }

        Sessions[Context.ConnectionId] = session;
        return Task.CompletedTask;
    }

    public Task AppendAudio(string base64Chunk)
    {
        if (string.IsNullOrWhiteSpace(base64Chunk))
        {
            return Task.CompletedTask;
        }

        if (!Sessions.TryGetValue(Context.ConnectionId, out var session))
        {
            throw new HubException("No active voice session.");
        }

        byte[] chunk;
        try
        {
            chunk = Convert.FromBase64String(base64Chunk);
        }
        catch (FormatException)
        {
            throw new HubException("Invalid audio chunk.");
        }

        session.Gate.Wait();
        try
        {
            if (session.Closed)
            {
                return Task.CompletedTask;
            }

            if (session.Buffer.Length + chunk.Length > SpeechToTextLimits.MaxAudioBytes)
            {
                throw new HubException($"Audio must be at most {SpeechToTextLimits.MaxAudioBytes} bytes.");
            }

            session.Buffer.Write(chunk, 0, chunk.Length);
            MaybeSchedulePartial(session);
        }
        finally
        {
            session.Gate.Release();
        }

        return Task.CompletedTask;
    }

    public async Task<TranscribeAudioResponseDto> FinishSession(string? contextHint)
    {
        if (!Sessions.TryGetValue(Context.ConnectionId, out var session))
        {
            throw new HubException("No active voice session.");
        }

        byte[] audio;
        string contentType;
        string fileName;
        int sampleRate;
        string? lastGoodRaw;
        int lastGoodLength;
        DateTimeOffset lastGoodUtc;
        var lastPartialWasTail = false;
        CancellationToken sessionCancel;
        session.Gate.Wait();
        try
        {
            if (session.Closed)
            {
                throw new HubException("No active voice session.");
            }

            session.Closed = true;
            sessionCancel = session.Cts.Token;
            audio = session.Buffer.ToArray();
            contentType = session.ContentType;
            fileName = session.FileName;
            sampleRate = session.SampleRate;
            lastGoodRaw = session.LastGoodRawTranscript;
            lastGoodLength = session.LastGoodBufferLength;
            lastGoodUtc = session.LastGoodUtc;
            lastPartialWasTail = session.LastPartialWasTailWindow;
        }
        finally
        {
            session.Gate.Release();
        }

        var pcm = PcmWavWriter.IsRawPcm(contentType);
        var maxNewBytes = pcm
            ? VoiceTranscriptReusePolicy.PcmMaxNewBytes(sampleRate)
            : VoiceTranscriptReusePolicy.DefaultMaxNewBytes;
        var maxAge = pcm ? VoiceTranscriptReusePolicy.PcmMaxAge : VoiceTranscriptReusePolicy.DefaultMaxAge;

        if (VoiceTranscriptReusePolicy.ShouldReuse(
                lastGoodRaw,
                lastGoodLength,
                lastGoodUtc,
                audio.Length,
                DateTimeOffset.UtcNow,
                maxNewBytes,
                maxAge,
                lastPartialWasTailWindow: lastPartialWasTail))
        {
            Sessions.TryRemove(Context.ConnectionId, out _);
            DisposeSessionInBackground(session);
            return ToTranscriptDto(lastGoodRaw!, contextHint);
        }

        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                Context.ConnectionAborted,
                sessionCancel);
            await session.TranscribeLock.WaitAsync(linked.Token);
            try
            {
                if (VoiceTranscriptReusePolicy.ShouldReuse(
                        session.LastGoodRawTranscript,
                        session.LastGoodBufferLength,
                        session.LastGoodUtc,
                        audio.Length,
                        DateTimeOffset.UtcNow,
                        maxNewBytes,
                        maxAge,
                        lastPartialWasTailWindow: session.LastPartialWasTailWindow))
                {
                    return ToTranscriptDto(session.LastGoodRawTranscript!, contextHint);
                }

                return await TranscribeAsync(audio, contentType, fileName, sampleRate, contextHint, linked.Token);
            }
            finally
            {
                session.TranscribeLock.Release();
            }
        }
        finally
        {
            if (Sessions.TryRemove(Context.ConnectionId, out var removed))
            {
                removed.Dispose();
            }
            else
            {
                session.Dispose();
            }
        }
    }

    public Task AbortSession()
    {
        if (Sessions.TryGetValue(Context.ConnectionId, out var session))
        {
            session.Closed = true;
            try
            {
                session.Cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // already cleaned up
            }
        }

        if (Sessions.TryRemove(Context.ConnectionId, out var removed))
        {
            removed.Dispose();
        }

        return Task.CompletedTask;
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        if (Sessions.TryRemove(Context.ConnectionId, out var session))
        {
            session.Dispose();
        }

        return base.OnDisconnectedAsync(exception);
    }

    private static TranscribeAudioResponseDto ToTranscriptDto(string lastGoodRaw, string? contextHint)
    {
        var raw = lastGoodRaw.Trim();
        var normalized = VoiceTranscriptNormalizer.Normalize(raw, contextHint).Trim();
        return new TranscribeAudioResponseDto
        {
            RawTranscript = raw,
            Transcript = string.IsNullOrWhiteSpace(normalized) ? raw : normalized
        };
    }

    private static void DisposeSessionInBackground(Session session)
    {
        try
        {
            session.Cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // already cleaned up
        }

        _ = Task.Run(async () =>
        {
            try
            {
                if (await session.TranscribeLock.WaitAsync(TimeSpan.FromSeconds(8)))
                {
                    session.TranscribeLock.Release();
                }
            }
            catch (ObjectDisposedException)
            {
                // already cleaned up
            }
            finally
            {
                session.Dispose();
            }
        });
    }

    private void MaybeSchedulePartial(Session session)
    {
        if (session.PartialInFlight)
        {
            return;
        }

        var pcm = PcmWavWriter.IsRawPcm(session.ContentType);
        var minBytes = pcm
            ? Math.Max(PartialMinBytes, session.SampleRate * 2)
            : PartialMinBytes;
        if (session.Buffer.Length < minBytes)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var minInterval = pcm ? PcmPartialMinInterval : PartialMinInterval;
        if (now - session.LastPartialUtc < minInterval)
        {
            return;
        }

        session.PartialInFlight = true;
        session.LastPartialUtc = now;
        var snapshot = session.Buffer.ToArray();
        var connectionId = Context.ConnectionId;
        var contentType = session.ContentType;
        var fileName = session.FileName;
        var sampleRate = session.SampleRate;
        var sessionCancel = session.Cts.Token;
        var sttSnapshot = snapshot;
        var partialWasTail = false;
        if (pcm)
        {
            (sttSnapshot, partialWasTail) = VoicePcmPartialWindow.Slice(snapshot, sampleRate);
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await session.TranscribeLock.WaitAsync(sessionCancel);
                try
                {
                    var dto = await TranscribeAsync(
                        sttSnapshot,
                        contentType,
                        fileName,
                        sampleRate,
                        contextHint: null,
                        sessionCancel);
                    if (string.IsNullOrWhiteSpace(dto.RawTranscript) && string.IsNullOrWhiteSpace(dto.Transcript))
                    {
                        return;
                    }

                    if (!Sessions.TryGetValue(connectionId, out var live))
                    {
                        return;
                    }

                    live.LastGoodRawTranscript = string.IsNullOrWhiteSpace(dto.RawTranscript)
                        ? dto.Transcript
                        : dto.RawTranscript;
                    live.LastGoodBufferLength = snapshot.Length;
                    live.LastGoodUtc = DateTimeOffset.UtcNow;
                    live.LastPartialWasTailWindow = partialWasTail;

                    if (live.Closed || string.IsNullOrWhiteSpace(dto.Transcript))
                    {
                        return;
                    }

                    await Clients.Client(connectionId).SendAsync(PartialTranscriptMethod, dto.Transcript);
                }
                finally
                {
                    session.TranscribeLock.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Ignoring incomplete-audio partial transcription");
            }
            finally
            {
                if (Sessions.TryGetValue(connectionId, out var live))
                {
                    live.Gate.Wait();
                    try
                    {
                        live.PartialInFlight = false;
                        if (!live.Closed)
                        {
                            MaybeSchedulePartial(live);
                        }
                    }
                    finally
                    {
                        live.Gate.Release();
                    }
                }
            }
        });
    }

    private async Task<TranscribeAudioResponseDto> TranscribeAsync(
        byte[] audio,
        string contentType,
        string fileName,
        int sampleRate,
        string? contextHint,
        CancellationToken cancellationToken)
    {
        if (audio.Length == 0)
        {
            throw new HubException("No audio was captured.");
        }

        var sttContentType = contentType;
        var sttFileName = fileName;
        var sttAudio = audio;
        if (PcmWavWriter.IsRawPcm(contentType))
        {
            sttAudio = PcmWavWriter.WrapPcm16Mono(audio, sampleRate);
            sttContentType = PcmWavWriter.WavContentType;
            sttFileName = "recording.wav";
        }

        var command = new TranscribeAudioCommand
        {
            AudioContent = sttAudio,
            ContentType = sttContentType,
            FileName = sttFileName,
            ContextHint = contextHint
        };

        var validation = await _validator.ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
        {
            throw new HubException(validation.Errors[0].ErrorMessage);
        }

        try
        {
            var result = await _mediator.Send(command, cancellationToken);
            return result.Payload;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (FeatureDisabledException ex)
        {
            throw new HubException(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            throw new HubException(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Voice hub transcription failed");
            throw new HubException("Transcription failed.");
        }
    }

    private sealed class Session : IDisposable
    {
        public MemoryStream Buffer { get; } = new();
        public string ContentType { get; set; } = "audio/webm";
        public string FileName { get; set; } = "recording.webm";
        public int SampleRate { get; set; } = 16000;
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public SemaphoreSlim TranscribeLock { get; } = new(1, 1);
        public CancellationTokenSource Cts { get; } = new();
        public DateTimeOffset LastPartialUtc { get; set; } = DateTimeOffset.MinValue;
        public bool PartialInFlight { get; set; }
        public string? LastGoodRawTranscript { get; set; }
        public int LastGoodBufferLength { get; set; }
        public DateTimeOffset LastGoodUtc { get; set; } = DateTimeOffset.MinValue;
        public bool LastPartialWasTailWindow { get; set; }
        public bool Closed { get; set; }

        public void Dispose()
        {
            try
            {
                Cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // already cleaned up
            }

            Cts.Dispose();
            Buffer.Dispose();
            Gate.Dispose();
            TranscribeLock.Dispose();
        }
    }
}
