using System;
using System.Linq;
using Fistix.TaskManager.ViewModel.Commands.Todos;
using FluentValidation;

namespace Fistix.TaskManager.ViewModel.Validators.Todos;

/// <summary>
/// Static limits aligned with default Ai:SpeechToText settings (handler also enforces config).
/// </summary>
public static class SpeechToTextLimits
{
    public const int MaxAudioBytes = 5 * 1024 * 1024;
    public const int MaxFileNameLength = 200;

    public static readonly string[] AllowedContentTypes =
    [
        "audio/webm",
        "audio/pcm",
        "audio/l16",
        "audio/wav",
        "audio/x-wav",
        "audio/mpeg",
        "audio/mp4",
        "audio/ogg",
        "audio/flac",
        "application/octet-stream"
    ];

    public static bool IsAllowedContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return false;
        }

        var mediaType = contentType.Split(';', 2)[0].Trim();
        return AllowedContentTypes.Any(a =>
            string.Equals(a, mediaType, StringComparison.OrdinalIgnoreCase));
    }
}

public class TranscribeAudioCommandValidator : AbstractValidator<TranscribeAudioCommand>
{
    public TranscribeAudioCommandValidator()
    {
        RuleFor(x => x.AudioContent)
            .NotNull()
            .Must(bytes => bytes is { Length: > 0 })
            .WithMessage("Audio content is required.")
            .Must(bytes => bytes is null || bytes.Length <= SpeechToTextLimits.MaxAudioBytes)
            .WithMessage($"Audio must be at most {SpeechToTextLimits.MaxAudioBytes} bytes.");

        RuleFor(x => x.ContentType)
            .NotEmpty()
            .Must(SpeechToTextLimits.IsAllowedContentType)
            .WithMessage("Unsupported audio content type.");

        RuleFor(x => x.FileName)
            .NotEmpty()
            .MaximumLength(SpeechToTextLimits.MaxFileNameLength);
    }
}
