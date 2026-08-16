#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using Fistix.TaskManager.AiLayer.Shared;
using Fistix.TaskManager.ViewModel.Queries.Todos;
using MediatR;

namespace Fistix.TaskManager.ServiceLayer.Todos;

public class GetVoiceTranscriptionOptionsQueryHandler
    : IRequestHandler<GetVoiceTranscriptionOptionsQuery, GetVoiceTranscriptionOptionsQueryResult>
{
    private readonly AiConfiguration _aiConfig;

    public GetVoiceTranscriptionOptionsQueryHandler(AiConfiguration aiConfig)
    {
        _aiConfig = aiConfig;
    }

    public Task<GetVoiceTranscriptionOptionsQueryResult> Handle(
        GetVoiceTranscriptionOptionsQuery request,
        CancellationToken cancellationToken)
    {
        var settings = _aiConfig.SpeechToText;
        var sampleRate = settings.PcmSampleRate > 0 ? settings.PcmSampleRate : 16000;
        var batchMs = settings.LiveCaptionBatchMs > 0 ? settings.LiveCaptionBatchMs : 300;

        return Task.FromResult(new GetVoiceTranscriptionOptionsQueryResult
        {
            Payload = new VoiceTranscriptionOptionsDto
            {
                EnableLocalLiveCaptions = settings.EnableLocalLiveCaptions,
                PcmSampleRate = Math.Clamp(sampleRate, 8000, 48000),
                LiveCaptionBatchMs = Math.Clamp(batchMs, 200, 400)
            }
        });
    }
}
