#nullable enable

using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Fistix.TaskManager.AiLayer.Abstractions;
using Fistix.TaskManager.AiLayer.Shared;
using Fistix.TaskManager.Core.Exceptions;
using Fistix.TaskManager.ServiceLayer.Todos;
using Fistix.TaskManager.ViewModel.Commands.Todos;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Fistix.TaskManager.ServiceLayer.Tests;

public class TranscribeAudioCommandHandlerTests
{
    [Fact]
    public async Task Handle_Throws_WhenFeatureDisabled()
    {
        var handler = CreateHandler(
            enabled: false,
            stt: new FakeSpeechToText("buy milk tomorrow"));

        await Assert.ThrowsAsync<FeatureDisabledException>(() =>
            handler.Handle(ValidCommand(), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_ReturnsTranscript_WhenEnabled()
    {
        var handler = CreateHandler(
            enabled: true,
            stt: new FakeSpeechToText("  buy milk tomorrow  "));

        var result = await handler.Handle(ValidCommand(), CancellationToken.None);

        Assert.Equal("buy milk tomorrow", result.Payload.Transcript);
    }

    [Fact]
    public async Task Handle_Throws_WhenTranscriptEmpty()
    {
        var handler = CreateHandler(
            enabled: true,
            stt: new FakeSpeechToText("   "));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(ValidCommand(), CancellationToken.None));
    }

    private static TranscribeAudioCommand ValidCommand() => new()
    {
        AudioContent = [1, 2, 3, 4],
        ContentType = "audio/webm",
        FileName = "recording.webm"
    };

    private static TranscribeAudioCommandHandler CreateHandler(bool enabled, ISpeechToTextService stt)
    {
        var config = new AiConfiguration
        {
            Features = new AiFeaturesConfiguration
            {
                EnableVoiceTranscription = enabled
            },
            SpeechToText = new SpeechToTextSettings
            {
                Endpoint = "http://stt.test.local",
                MaxAudioBytes = 5 * 1024 * 1024
            }
        };

        return new TranscribeAudioCommandHandler(
            stt,
            config,
            NullLogger<TranscribeAudioCommandHandler>.Instance);
    }

    private sealed class FakeSpeechToText : ISpeechToTextService
    {
        private readonly string _transcript;

        public FakeSpeechToText(string transcript) => _transcript = transcript;

        public Task<string> TranscribeAsync(
            Stream audioStream,
            string fileName,
            string contentType,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_transcript);
    }
}
