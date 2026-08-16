#nullable enable

using System.Text;
using Fistix.TaskManager.AiLayer.Shared;

namespace Fistix.TaskManager.AiLayer.Tests;

public class PcmWavWriterTests
{
    [Fact]
    public void IsRawPcm_RecognizesPcmAndL16()
    {
        Assert.True(PcmWavWriter.IsRawPcm("audio/pcm"));
        Assert.True(PcmWavWriter.IsRawPcm("audio/l16;rate=16000"));
        Assert.False(PcmWavWriter.IsRawPcm("audio/webm"));
    }

    [Fact]
    public void WrapPcm16Mono_WritesRiffHeaderAndPayload()
    {
        var pcm = new byte[] { 0x01, 0x00, 0x02, 0x00 };
        var wav = PcmWavWriter.WrapPcm16Mono(pcm, 16000);

        Assert.Equal(PcmWavWriter.HeaderBytes + pcm.Length, wav.Length);
        Assert.Equal("RIFF", Encoding.ASCII.GetString(wav, 0, 4));
        Assert.Equal("WAVE", Encoding.ASCII.GetString(wav, 8, 4));
        Assert.Equal("fmt ", Encoding.ASCII.GetString(wav, 12, 4));
        Assert.Equal("data", Encoding.ASCII.GetString(wav, 36, 4));
        Assert.Equal(pcm[0], wav[44]);
        Assert.Equal(pcm[3], wav[47]);

        var sampleRate = BitConverter.ToInt32(wav, 24);
        Assert.Equal(16000, sampleRate);
        var dataSize = BitConverter.ToInt32(wav, 40);
        Assert.Equal(pcm.Length, dataSize);
    }

    [Fact]
    public void WrapPcm16Mono_EmptyReturnsEmpty()
    {
        Assert.Empty(PcmWavWriter.WrapPcm16Mono([], 16000));
    }
}
