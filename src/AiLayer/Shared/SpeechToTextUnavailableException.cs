using System;

namespace Fistix.TaskManager.AiLayer.Shared;

public sealed class SpeechToTextUnavailableException : Exception
{
    public SpeechToTextUnavailableException(string message, int retryAfterSeconds = 15)
        : base(message)
    {
        RetryAfterSeconds = retryAfterSeconds;
    }

    public int RetryAfterSeconds { get; }
}
