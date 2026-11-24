namespace Hearwood.Audio;

/// <summary>
/// Shared float→PCM16 and RIFF/WAV container helpers used by the engines
/// whose pipelines emit raw float samples (Kokoro, Pocket).
/// </summary>
public static class PcmWav
{
    /// <summary>Converts float samples (-1..1) into 16-bit little-endian mono PCM bytes.</summary>
    public static byte[] Pcm16FromFloat(float[] samples)
    {
        var pcm = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            float value = Math.Clamp(samples[i], -1f, 1f);
            short quantized = (short)MathF.Round(value * short.MaxValue);
            pcm[2 * i] = (byte)(quantized & 0xFF);
            pcm[2 * i + 1] = (byte)((quantized >> 8) & 0xFF);
        }
        return pcm;
    }

    /// <summary>Wraps raw 16-bit mono PCM into a RIFF/WAV container.</summary>
    public static byte[] WrapPcmToWav(byte[] pcm, int sampleRate)
    {
        const short pcmFormat = 1;
        short channels = 1, bitsPerSample = 16;
        short blockAlign = (short)(channels * bitsPerSample / 8);
        int byteRate = sampleRate * blockAlign;

        var header = new byte[44];

        header[0] = 0x52; header[1] = 0x49; header[2] = 0x46; header[3] = 0x46; // "RIFF"
        WriteInt(header, 4, 36 + pcm.Length);
        header[8] = 0x57; header[9] = 0x41; header[10] = 0x56; header[11] = 0x45; // "WAVE"
        // "fmt " + 16
        header[12] = 0x66; header[13] = 0x6D; header[14] = 0x74; header[15] = 0x20;
        WriteInt(header, 16, 16);
        header[20] = (byte)pcmFormat;
        header[21] = 0;
        header[22] = (byte)channels;
        header[23] = 0;
        WriteInt(header, 24, sampleRate);
        WriteInt(header, 28, byteRate);
        header[32] = (byte)blockAlign;
        header[34] = (byte)bitsPerSample;
        // "data" + size
        header[36] = 0x64; header[37] = 0x61; header[38] = 0x74; header[39] = 0x61;
        WriteInt(header, 40, pcm.Length);

        var result = new byte[44 + pcm.Length];
        header.CopyTo(result, 0);
        pcm.CopyTo(result, 44);
        return result;

        static void WriteInt(byte[] target, int at, int value)
        {
            target[at] = (byte)value;
            target[at + 1] = (byte)(value >> 8);
            target[at + 2] = (byte)(value >> 16);
            target[at + 3] = (byte)(value >> 24);
        }
    }
}
