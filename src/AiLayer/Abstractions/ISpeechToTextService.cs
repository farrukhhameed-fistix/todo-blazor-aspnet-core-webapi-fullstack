using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Fistix.TaskManager.AiLayer.Abstractions;

public interface ISpeechToTextService
{
    Task<string> TranscribeAsync(
        Stream audioStream,
        string fileName,
        string contentType,
        CancellationToken cancellationToken = default);
}
