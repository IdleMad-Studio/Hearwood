namespace Hearwood.Audio;

/// <summary>
/// Trims leading/trailing silence from a synthesized batch, ported from
/// kokoro-onnx's librosa-derived <c>trim()</c>: frame RMS with a 2048-sample
/// window / 512 hop, anything 60 dB below the loudest frame counts as silence.
/// Used to drop the model's cold onset and tail while keeping phoneme timings
/// aligned — returned head offset shifts every timing edge.
/// </summary>
public static class SilenceTrim
{
    /// <summary>
    /// Trims silence. Returns the trimmed buffer and the number of samples
    /// dropped from the head (timings shift by exactly this). Defensive: when
    /// the frame scan finds no audible content, the buffer is returned intact
    /// (librosa would return empty, which breaks downstream slicing).
    /// </summary>
    public static (float[] Trimmed, int HeadSamples) TrimSilence(
        float[] samples,
        int frameLength = 2048,
        int hopLength = 512,
        double topDb = 60)
    {
        // Center-pad by half a frame so frame i is centered at sample i*hop.
        int pad = frameLength / 2;
        int paddedLength = samples.Length + 2 * pad;
        int frameCount = paddedLength >= frameLength ? (paddedLength - frameLength) / hopLength + 1 : 0;

        int firstNonSilent = -1, lastNonSilent = -1;
        double reference = 0;

        // First pass: frame energies, tracked loudly enough to get the max RMS.
        // rms values are computed on demand in pass 2; here just find max.
        var rms = new double[frameCount];
        for (int f = 0; f < frameCount; f++)
        {
            int offset = f * hopLength; // index into the padded buffer
            double sum = 0;
            for (int i = 0; i < frameLength; i++)
            {
                int idx = offset + i - pad; // padded buffer coordinate -> original sample index
                double value = idx >= 0 && idx < samples.Length ? samples[idx] : 0.0;
                sum += value * value;
            }
            rms[f] = Math.Sqrt(sum / frameLength);
            if (rms[f] > reference)
                reference = rms[f];
        }

        if (frameCount == 0 || reference <= 0)
            return (samples, 0);

        // amplitude dB relative to the loudest frame, threshold topDb below it.
        double thresholdDb = -topDb;
        for (int f = 0; f < frameCount; f++)
        {
            double value = Math.Max(rms[f], 1e-5);
            double db = 20 * Math.Log10(value / Math.Max(reference, 1e-5));
            bool nonSilent = db > thresholdDb;

            if (nonSilent)
            {
                if (firstNonSilent < 0)
                    firstNonSilent = f;
                lastNonSilent = f;
            }
        }

        if (firstNonSilent < 0)
            return (samples, 0);

        int start = firstNonSilent * hopLength;
        int end = Math.Min(samples.Length, (lastNonSilent + 1) * hopLength);
        if (start <= 0 && end >= samples.Length)
            return (samples, 0);

        int length = Math.Max(0, end - start);
        var trimmed = new float[length];
        Array.Copy(samples, start, trimmed, 0, length);
        return (trimmed, start);
    }
}
