#nullable enable

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fistix.TaskManager.AiLayer.Abstractions;
using Fistix.TaskManager.AiLayer.Shared;
using Fistix.TaskManager.Core.Exceptions;
using Fistix.TaskManager.ViewModel.Commands.Todos;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Fistix.TaskManager.ServiceLayer.Todos;

public class TranscribeAudioCommandHandler : IRequestHandler<TranscribeAudioCommand, TranscribeAudioCommandResult>
{
    private readonly ISpeechToTextService _speechToText;
    private readonly AiConfiguration _aiConfig;
    private readonly ILogger<TranscribeAudioCommandHandler> _logger;

    public TranscribeAudioCommandHandler(
        ISpeechToTextService speechToText,
        AiConfiguration aiConfig,
        ILogger<TranscribeAudioCommandHandler> logger)
    {
        _speechToText = speechToText;
        _aiConfig = aiConfig;
        _logger = logger;
    }

    public async Task<TranscribeAudioCommandResult> Handle(
        TranscribeAudioCommand command,
        CancellationToken cancellationToken)
    {
        if (!_aiConfig.Features.EnableVoiceTranscription)
        {
            throw new FeatureDisabledException("AI voice transcription");
        }

        ValidateAgainstConfig(command);

        _logger.LogInformation(
            "Transcribing audio upload ({Length} bytes, type {ContentType})",
            command.AudioContent.Length,
            command.ContentType);

        await using var stream = new MemoryStream(command.AudioContent, writable: false);
        var transcript = await _speechToText.TranscribeAsync(
            stream,
            command.FileName,
            command.ContentType,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(transcript))
        {
            throw new InvalidOperationException("No speech was detected in the audio.");
        }

        return new TranscribeAudioCommandResult
        {
            Payload = new TranscribeAudioResponseDto { Transcript = transcript.Trim() }
        };
    }

    private void ValidateAgainstConfig(TranscribeAudioCommand command)
    {
        var settings = _aiConfig.SpeechToText;
        var maxBytes = settings.MaxAudioBytes > 0
            ? settings.MaxAudioBytes
            : 5 * 1024 * 1024;

        if (command.AudioContent.Length > maxBytes)
        {
            throw new InvalidOperationException($"Audio must be at most {maxBytes} bytes.");
        }

        var allowed = settings.AllowedContentTypes is { Length: > 0 }
            ? settings.AllowedContentTypes
            : ["audio/webm", "audio/wav", "application/octet-stream"];

        var mediaType = (command.ContentType ?? string.Empty).Split(';', 2)[0].Trim();
        if (!allowed.Any(a => string.Equals(a, mediaType, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Unsupported audio content type.");
        }
    }
}
