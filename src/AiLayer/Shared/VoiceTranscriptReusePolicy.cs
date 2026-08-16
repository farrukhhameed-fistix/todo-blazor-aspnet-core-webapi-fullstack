#nullable enable

using System;

namespace Fistix.TaskManager.AiLayer.Shared;

/// <summary>
/// Decides when FinishSession can reuse the last successful partial STT instead of calling Whisper again.
/// </summary>
public static class VoiceTranscriptReusePolicy
{
    public static readonly TimeSpan DefaultMaxAge = TimeSpan.FromSeconds(1.5);
    public const int DefaultMaxNewBytes = 8 * 1024;

    /// <summary>PCM live captions: reuse a recent Whisper partial instead of a second full STT.</summary>
    public static readonly TimeSpan PcmMaxAge = TimeSpan.FromSeconds(5);

    public static int PcmMaxNewBytes(int sampleRateHz)
    {
        var rate = sampleRateHz > 0 ? sampleRateHz : 16000;
        return Math.Max(DefaultMaxNewBytes, rate * 2 * 2);
    }

    public static bool ShouldReuse(
        string? lastGoodRaw,
        int lastGoodBufferLength,
        DateTimeOffset lastGoodUtc,
        int currentBufferLength,
        DateTimeOffset now,
        int maxNewBytes = DefaultMaxNewBytes,
        TimeSpan? maxAge = null,
        bool lastPartialWasTailWindow = false)
    {
        if (lastPartialWasTailWindow || string.IsNullOrWhiteSpace(lastGoodRaw))
        {
            return false;
        }

        var newBytes = Math.Max(0, currentBufferLength - lastGoodBufferLength);
        if (newBytes <= maxNewBytes)
        {
            return true;
        }

        return now - lastGoodUtc <= (maxAge ?? DefaultMaxAge);
    }
}
