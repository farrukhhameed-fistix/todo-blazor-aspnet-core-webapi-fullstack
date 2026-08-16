#nullable enable

using MediatR;

namespace Fistix.TaskManager.ViewModel.Queries.Todos;

public class GetVoiceTranscriptionOptionsQuery : IRequest<GetVoiceTranscriptionOptionsQueryResult>
{
}

public class GetVoiceTranscriptionOptionsQueryResult
{
    public VoiceTranscriptionOptionsDto Payload { get; set; } = new();
}

public class VoiceTranscriptionOptionsDto
{
    public bool EnableLocalLiveCaptions { get; set; }

    public int PcmSampleRate { get; set; } = 16000;

    public int LiveCaptionBatchMs { get; set; } = 300;
}
