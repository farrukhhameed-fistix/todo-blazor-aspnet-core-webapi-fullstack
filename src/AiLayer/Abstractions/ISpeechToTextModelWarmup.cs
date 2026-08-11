namespace Fistix.TaskManager.AiLayer.Abstractions;

public interface ISpeechToTextModelWarmup
{
    void EnsureModelInBackground();
    bool IsReady { get; }
    bool IsWarmingUp { get; }
    string? LastError { get; }
}
