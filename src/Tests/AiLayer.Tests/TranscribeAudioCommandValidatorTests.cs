using Fistix.TaskManager.ViewModel.Commands.Todos;
using Fistix.TaskManager.ViewModel.Validators.Todos;
using FluentValidation.TestHelper;

namespace Fistix.TaskManager.AiLayer.Tests;

public class TranscribeAudioCommandValidatorTests
{
    private readonly TranscribeAudioCommandValidator _validator = new();

    [Fact]
    public void Should_fail_when_audio_empty()
    {
        var command = new TranscribeAudioCommand
        {
            AudioContent = [],
            ContentType = "audio/webm",
            FileName = "recording.webm"
        };

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.AudioContent);
    }

    [Fact]
    public void Should_fail_when_content_type_unsupported()
    {
        var command = new TranscribeAudioCommand
        {
            AudioContent = [1, 2, 3],
            ContentType = "text/plain",
            FileName = "recording.webm"
        };

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.ContentType);
    }

    [Fact]
    public void Should_pass_for_valid_webm()
    {
        var command = new TranscribeAudioCommand
        {
            AudioContent = [1, 2, 3],
            ContentType = "audio/webm;codecs=opus",
            FileName = "recording.webm"
        };

        var result = _validator.TestValidate(command);

        result.ShouldNotHaveAnyValidationErrors();
    }
}
