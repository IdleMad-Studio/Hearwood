using Hearwood.Audio;

namespace Hearwood.Tests;

public class SonicTests
{
    const int Rate = 24000;

    /// <summary>A voiced sound: a 140 Hz buzz with a few harmonics, rising and falling in loudness.</summary>
    static float[] Voice(double seconds, double hz = 140)
    {
        var samples = new float[(int)(seconds * Rate)];
        for (int i = 0; i < samples.Length; i++)
        {
            double t = i / (double)Rate;
            double envelope = 0.5 + 0.4 * Math.Sin(2 * Math.PI * 1.5 * t);
            double buzz = Math.Sin(2 * Math.PI * hz * t) + 0.5 * Math.Sin(4 * Math.PI * hz * t) + 0.25 * Math.Sin(6 * Math.PI * hz * t);
            samples[i] = (float)(0.3 * envelope * buzz);
        }
        return samples;
    }

    /// <summary>Feeds the input in 20 ms blocks, as SpeechStream does, reading the speed before each.</summary>
    static float[] Stretch(float[] input, Func<int, double> speedAt)
    {
        var sonic = new Sonic(Rate);
        var output = new List<float>();
        var buffer = new float[1000];
        for (int at = 0; at < input.Length; at += 480)
        {
            sonic.Speed = speedAt(at);
            sonic.Write(input.AsSpan(at, Math.Min(480, input.Length - at)));
            Drain();
        }
        sonic.Flush();
        Drain();
        return [.. output];

        void Drain()
        {
            int n;
            while ((n = sonic.Read(buffer)) > 0)
                output.AddRange(buffer.AsSpan(0, n));
        }
    }

    /// <summary>The strongest period between 2.5 and 10 ms (100–400 Hz) by autocorrelation, in Hz.</summary>
    static double PitchOf(float[] samples)
    {
        var middle = samples.AsSpan(samples.Length / 4, samples.Length / 2);
        int best = 0;
        double bestScore = double.MinValue;
        for (int lag = Rate / 400; lag <= Rate / 100; lag++)
        {
            double score = 0;
            for (int i = 0; i + lag < middle.Length; i++)
                score += middle[i] * middle[i + lag];
            score /= middle.Length - lag;
            if (score > bestScore)
            {
                bestScore = score;
                best = lag;
            }
        }
        return Rate / (double)best;
    }

    [Theory]
    [InlineData(0.75)]
    [InlineData(1.25)]
    [InlineData(2.0)]
    [InlineData(3.0)]
    public void Speed_changes_the_length_but_not_the_pitch(double speed)
    {
        var input = Voice(3);

        var output = Stretch(input, _ => speed);

        Assert.InRange(output.Length, input.Length / speed * 0.98, input.Length / speed * 1.02);
        Assert.InRange(PitchOf(output), 135, 145);
    }

    [Fact]
    public void At_normal_speed_the_samples_pass_through_unchanged()
    {
        var input = Voice(1);

        Assert.Equal(input, Stretch(input, _ => 1.0));
    }

    [Fact]
    public void A_speed_change_mid_stream_applies_from_then_on()
    {
        var input = Voice(4);
        int half = input.Length / 2;

        var output = Stretch(input, at => at < half ? 1.0 : 2.0);

        double expected = half + (input.Length - half) / 2.0;
        Assert.InRange(output.Length, expected * 0.98, expected * 1.02);
    }

    [Fact]
    public void Every_output_sample_stays_within_the_inputs_range()
    {
        var input = Voice(2);
        float peak = input.Max(MathF.Abs);

        Assert.All(Stretch(input, _ => 2.5), s => Assert.InRange(MathF.Abs(s), 0, peak));
    }
}
