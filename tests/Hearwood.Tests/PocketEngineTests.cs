using Hearwood.Pocket;

namespace Hearwood.Tests;

public sealed class PocketEngineTests
{
    const int Rate = PocketEngine.SampleRate;

    /// <summary>A call's audio as ptts gives it: the decoder's tick, the model's leading silence, then speech.</summary>
    static float[] Call(int tickMs, int speechAtMs, int speechMs)
    {
        var random = new Random(1);
        var audio = new float[Rate * (speechAtMs + speechMs) / 1000];
        for (int i = 0; i < audio.Length; i++)
            audio[i] = (float)((random.NextDouble() - 0.5) * 2e-4); // about 70 dB below the speech
        for (int i = 0; i < Rate * tickMs / 1000; i++)
            audio[i] += 0.008f * MathF.Exp(-i / 48f); // about 40 dB below the speech, as measured
        for (int i = Rate * speechAtMs / 1000; i < audio.Length; i++)
            audio[i] = 0.5f * MathF.Sin(2 * MathF.PI * 220 * i / Rate);
        return audio;
    }

    [Fact]
    public void The_decoders_warm_up_tick_does_not_keep_the_leading_silence()
    {
        var finished = PocketEngine.Finish(Call(tickMs: 20, speechAtMs: 700, speechMs: 1000));

        // The trim keeps at most one of its frames (2048 samples) of lead-in before the speech.
        Assert.InRange(finished.Length, Rate, Rate + 2048);
    }

    [Fact]
    public void A_call_that_speaks_from_its_first_sample_keeps_its_start()
    {
        // ptts speaks "I." or "1984 was a year." from its first few ms, with no silence after the tick.
        var call = Call(tickMs: 0, speechAtMs: 0, speechMs: 1000);
        Assert.Same(call, PocketEngine.Finish(call));

        var speechAt20 = Call(tickMs: 20, speechAtMs: 20, speechMs: 1000);
        Assert.Equal(speechAt20.Length, PocketEngine.Finish(speechAt20).Length);

        var tiny = new float[PocketEngine.DecoderWarmUp];
        Assert.Same(tiny, PocketEngine.Finish(tiny));
    }
}
