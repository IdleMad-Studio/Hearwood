using Hearwood.Pocket;
using Hearwood.Text;

namespace Hearwood.Tests;

/// <summary>
/// The real engine through the stretched stream. Skipped unless the Phonon library is built and the model is
/// already in %APPDATA%\Hearwood (run the app once); the default voice's cache is written there if missing.
/// </summary>
public sealed class SpeechStreamTests : IDisposable
{
    readonly PocketService _pocket = new();

    public void Dispose() => _pocket.Dispose();

    void SkipUnlessReady()
    {
        var why = PocketEngine.WhyUnavailable();
        Skip.If(why is not null, why);
        var checkpoint = Path.Combine(PocketService.DataDir, "kyutai-" + PocketSetup.Checkpoint);
        Skip.IfNot(PocketEngine.FilesIn(checkpoint).All(File.Exists), "The Pocket model is not in place yet; run Hearwood once.");
    }

    (float[] Audio, List<WordSpan> Lit) Render(string text, double rate)
    {
        using var stream = new SpeechStream(_ => _pocket.ReadyAsync(), SpeechPlan.Build(text), PocketService.DefaultVoice, waitForSpeech: true)
        {
            Rate = () => rate,
        };
        stream.Start();
        var audio = new List<float>();
        var lit = new List<WordSpan>();
        var buffer = new float[240];
        int n;
        while ((n = stream.Read(buffer)) > 0)
        {
            audio.AddRange(buffer.AsSpan(0, n));
            if (stream.WordAt(audio.Count) is { } w && (lit.Count == 0 || lit[^1] != w))
                lit.Add(w);
        }
        return ([.. audio], lit);
    }

    [SkippableFact]
    public void Twice_the_speed_takes_half_the_time_and_every_word_is_lit_in_order()
    {
        SkipUnlessReady();
        const string text = "The keeper climbed the stairs. At the top, he lit the lamp.";

        var (normal, litNormal) = Render(text, 1.0);
        var (fast, litFast) = Render(text, 2.0);

        Assert.InRange(fast.Length / (double)normal.Length, 0.45, 0.55);
        var words = SpeechPlan.Build(text).SelectMany(p => p.Words).ToList();
        Assert.Equal(words, litNormal);
        Assert.Equal(words, litFast);
    }

    [SkippableFact]
    public void Each_piece_starts_with_its_speech_not_the_models_leading_silence()
    {
        SkipUnlessReady();
        var engine = _pocket.ReadyAsync().GetAwaiter().GetResult();

        var speech = engine.Speak("The keeper climbed the stairs.", PocketService.DefaultVoice, CancellationToken.None);

        // Without the decoder's warm-up the trim cuts the half second or more of silence before the first word.
        float peak = speech.Max(MathF.Abs);
        int firstLoud = Array.FindIndex(speech, s => MathF.Abs(s) > peak * 0.1f);
        Assert.InRange(firstLoud, 0, PocketEngine.SampleRate / 5);
    }

    [SkippableFact]
    public void The_whole_text_renders_at_natural_speed()
    {
        SkipUnlessReady();
        var engine = _pocket.ReadyAsync().GetAwaiter().GetResult();

        var audio = SpeechRender.Render(engine, "One. Two.", PocketService.DefaultVoice, CancellationToken.None);
        var (streamed, _) = Render("One. Two.", 1.0);

        // The stream adds the last pause; the speech is the same length.
        Assert.InRange(streamed.Length - audio.Length, 0, PocketEngine.SampleRate);
    }
}
