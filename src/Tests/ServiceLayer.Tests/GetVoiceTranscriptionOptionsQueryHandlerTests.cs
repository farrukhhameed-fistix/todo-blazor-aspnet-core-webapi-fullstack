#nullable enable

using Fistix.TaskManager.AiLayer.Shared;
using Fistix.TaskManager.ServiceLayer.Todos;
using Fistix.TaskManager.ViewModel.Queries.Todos;

namespace Fistix.TaskManager.ServiceLayer.Tests;

public class GetVoiceTranscriptionOptionsQueryHandlerTests
{
    [Fact]
    public async Task Handle_ReturnsLocalCaptionFlagAndClampedBatch()
    {
        var handler = new GetVoiceTranscriptionOptionsQueryHandler(new AiConfiguration
        {
            SpeechToText = new SpeechToTextSettings
            {
                EnableLocalLiveCaptions = true,
                PcmSampleRate = 16000,
                LiveCaptionBatchMs = 50
            }
        });

        var result = await handler.Handle(new GetVoiceTranscriptionOptionsQuery(), CancellationToken.None);

        Assert.True(result.Payload.EnableLocalLiveCaptions);
        Assert.Equal(16000, result.Payload.PcmSampleRate);
        Assert.Equal(200, result.Payload.LiveCaptionBatchMs);
    }

    [Fact]
    public async Task Handle_DefaultsOff()
    {
        var handler = new GetVoiceTranscriptionOptionsQueryHandler(new AiConfiguration());

        var result = await handler.Handle(new GetVoiceTranscriptionOptionsQuery(), CancellationToken.None);

        Assert.False(result.Payload.EnableLocalLiveCaptions);
        Assert.Equal(16000, result.Payload.PcmSampleRate);
        Assert.Equal(300, result.Payload.LiveCaptionBatchMs);
    }
}
