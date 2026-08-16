#nullable enable

using Fistix.TaskManager.AiLayer.Shared;

namespace Fistix.TaskManager.AiLayer.Tests;

public class VoiceTranscriptReusePolicyTests
{
    [Fact]
    public void ShouldReuse_FalseWhenNoLastGood()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.False(VoiceTranscriptReusePolicy.ShouldReuse(
            lastGoodRaw: null,
            lastGoodBufferLength: 0,
            lastGoodUtc: now,
            currentBufferLength: 1000,
            now: now));
    }

    [Fact]
    public void ShouldReuse_TrueWhenBufferBarelyGrew()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.True(VoiceTranscriptReusePolicy.ShouldReuse(
            "create a task",
            lastGoodBufferLength: 40_000,
            lastGoodUtc: now.AddSeconds(-10),
            currentBufferLength: 40_000 + 100,
            now: now));
    }

    [Fact]
    public void ShouldReuse_TrueWhenRecentEvenIfGrew()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.True(VoiceTranscriptReusePolicy.ShouldReuse(
            "create a task",
            lastGoodBufferLength: 10_000,
            lastGoodUtc: now.AddMilliseconds(-400),
            currentBufferLength: 10_000 + 20_000,
            now: now));
    }

    [Fact]
    public void ShouldReuse_FalseWhenStaleAndGrew()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.False(VoiceTranscriptReusePolicy.ShouldReuse(
            "create a task",
            lastGoodBufferLength: 10_000,
            lastGoodUtc: now.AddSeconds(-5),
            currentBufferLength: 10_000 + 30_000,
            now: now));
    }

    [Fact]
    public void ShouldReuse_PcmWindowAllowsTwoSecondsOfNewAudio()
    {
        var now = DateTimeOffset.UtcNow;
        var maxNew = VoiceTranscriptReusePolicy.PcmMaxNewBytes(16000);
        Assert.Equal(64_000, maxNew);

        Assert.True(VoiceTranscriptReusePolicy.ShouldReuse(
            "create a task",
            lastGoodBufferLength: 10_000,
            lastGoodUtc: now.AddSeconds(-4),
            currentBufferLength: 10_000 + 60_000,
            now: now,
            maxNewBytes: maxNew,
            maxAge: VoiceTranscriptReusePolicy.PcmMaxAge));
    }

    [Fact]
    public void ShouldReuse_FalseWhenLastPartialWasTailWindow()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.False(VoiceTranscriptReusePolicy.ShouldReuse(
            "trailing window only",
            lastGoodBufferLength: 200_000,
            lastGoodUtc: now,
            currentBufferLength: 200_100,
            now: now,
            lastPartialWasTailWindow: true));
    }
}
