#nullable enable

using System;

namespace Fistix.TaskManager.AiLayer.Shared;

/// <summary>
/// Overlapping-window slice for live PCM captions: decode only the last N seconds
/// so Speaches is not re-running faster-whisper on the full growing buffer.
/// Finish must not reuse a tail-window transcript as the full utterance.
/// </summary>
public static class VoicePcmPartialWindow
{
    public const int WindowSeconds = 5;
    public const int BytesPerSample = 2;

    public static int WindowBytes(int sampleRateHz)
    {
        var rate = sampleRateHz > 0 ? sampleRateHz : 16000;
        return rate * BytesPerSample * WindowSeconds;
    }

    /// <summary>
    /// Returns a 16-bit-aligned tail slice. <paramref name="isTail"/> is true when audio was clipped.
    /// </summary>
    public static (byte[] Slice, bool IsTail) Slice(byte[] pcm, int sampleRateHz)
    {
        ArgumentNullException.ThrowIfNull(pcm);
        if (pcm.Length == 0)
        {
            return ([], false);
        }

        var maxBytes = WindowBytes(sampleRateHz);
        if (maxBytes <= 0 || pcm.Length <= maxBytes)
        {
            return (pcm, false);
        }

        var start = pcm.Length - maxBytes;
        if ((start & 1) == 1)
        {
            start--;
        }

        if (start <= 0)
        {
            return (pcm, false);
        }

        var slice = new byte[pcm.Length - start];
        Buffer.BlockCopy(pcm, start, slice, 0, slice.Length);
        return (slice, true);
    }
}
