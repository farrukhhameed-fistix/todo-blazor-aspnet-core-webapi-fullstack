using MediatR;

namespace Fistix.TaskManager.ViewModel.Commands.Todos;

public class TranscribeAudioCommand : IRequest<TranscribeAudioCommandResult>
{
    public byte[] AudioContent { get; set; } = [];
    public string ContentType { get; set; } = string.Empty;
    public string FileName { get; set; } = "audio.webm";
}

public class TranscribeAudioCommandResult
{
    public TranscribeAudioResponseDto Payload { get; set; } = new();
}

public class TranscribeAudioResponseDto
{
    public string Transcript { get; set; } = string.Empty;
}
