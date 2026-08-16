#nullable enable

using Fistix.TaskManager.AiLayer.Shared;

namespace Fistix.TaskManager.AiLayer.Tests;

public class VoicePcmPartialWindowTests
{
    [Fact]
    public void Slice_ReturnsFullBufferWhenShorterThanWindow()
    {
        var pcm = new byte[16000 * 2 * 2];
        var (slice, isTail) = VoicePcmPartialWindow.Slice(pcm, 16000);

        Assert.Same(pcm, slice);
        Assert.False(isTail);
    }

    [Fact]
    public void Slice_TakesLastFiveSecondsWhenLonger()
    {
        var window = VoicePcmPartialWindow.WindowBytes(16000);
        var pcm = new byte[window + 400];
        pcm[0] = 0x11;
        pcm[^1] = 0x22;

        var (slice, isTail) = VoicePcmPartialWindow.Slice(pcm, 16000);

        Assert.True(isTail);
        Assert.Equal(window, slice.Length);
        Assert.Equal(0x22, slice[^1]);
        Assert.NotEqual(0x11, slice[0]);
    }

    [Fact]
    public void Slice_EmptyStaysEmpty()
    {
        var (slice, isTail) = VoicePcmPartialWindow.Slice([], 16000);
        Assert.Empty(slice);
        Assert.False(isTail);
    }
}
