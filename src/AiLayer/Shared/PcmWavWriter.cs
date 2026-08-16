#nullable enable

using System;
using System.Buffers.Binary;
using System.Text;

namespace Fistix.TaskManager.AiLayer.Shared;

/// <summary>
/// Builds a 16-bit mono WAV from raw PCM (little-endian samples).
/// </summary>
public static class PcmWavWriter
{
    public const int HeaderBytes = 44;
    public const string PcmContentType = "audio/pcm";
    public const string L16ContentType = "audio/l16";
    public const string WavContentType = "audio/wav";

    public static bool IsRawPcm(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return false;
        }

        var mediaType = contentType.Split(';', 2)[0].Trim();
        return string.Equals(mediaType, PcmContentType, StringComparison.OrdinalIgnoreCase)
               || string.Equals(mediaType, L16ContentType, StringComparison.OrdinalIgnoreCase);
    }

    public static byte[] WrapPcm16Mono(byte[] pcm, int sampleRate)
    {
        ArgumentNullException.ThrowIfNull(pcm);
        if (pcm.Length == 0)
        {
            return [];
        }

        if (sampleRate <= 0)
        {
            sampleRate = 16000;
        }

        var wav = new byte[HeaderBytes + pcm.Length];
        WriteHeader(wav.AsSpan(0, HeaderBytes), pcm.Length, sampleRate);
        pcm.CopyTo(wav, HeaderBytes);
        return wav;
    }

    private static void WriteHeader(Span<byte> header, int dataLength, int sampleRate)
    {
        const short channels = 1;
        const short bitsPerSample = 16;
        var byteRate = sampleRate * channels * (bitsPerSample / 8);
        short blockAlign = (short)(channels * (bitsPerSample / 8));

        Encoding.ASCII.GetBytes("RIFF").CopyTo(header);
        BinaryPrimitives.WriteInt32LittleEndian(header[4..], 36 + dataLength);
        Encoding.ASCII.GetBytes("WAVE").CopyTo(header[8..]);
        Encoding.ASCII.GetBytes("fmt ").CopyTo(header[12..]);
        BinaryPrimitives.WriteInt32LittleEndian(header[16..], 16);
        BinaryPrimitives.WriteInt16LittleEndian(header[20..], 1);
        BinaryPrimitives.WriteInt16LittleEndian(header[22..], channels);
        BinaryPrimitives.WriteInt32LittleEndian(header[24..], sampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(header[28..], byteRate);
        BinaryPrimitives.WriteInt16LittleEndian(header[32..], blockAlign);
        BinaryPrimitives.WriteInt16LittleEndian(header[34..], bitsPerSample);
        Encoding.ASCII.GetBytes("data").CopyTo(header[36..]);
        BinaryPrimitives.WriteInt32LittleEndian(header[40..], dataLength);
    }
}
