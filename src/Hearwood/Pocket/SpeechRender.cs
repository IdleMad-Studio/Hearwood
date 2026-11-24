using Hearwood.Audio;
using Hearwood.Text;

namespace Hearwood.Pocket;

/// <summary>Whole texts at 1×, with the plan's pauses, for the Debug-only <c>--speak-to</c> check and the tests.</summary>
public static class SpeechRender
{
    /// <param name="onPiece">Called after each piece with (done, total).</param>
    public static float[] Render(PocketEngine engine, string text, string voice, CancellationToken cancellationToken, Action<int, int>? onPiece = null)
    {
        var plan = SpeechPlan.Build(text);
        var samples = new List<float>(PocketEngine.SampleRate * 10);
        for (int i = 0; i < plan.Count; i++)
        {
            samples.AddRange(engine.Speak(plan[i].Text, voice, cancellationToken));
            if (i < plan.Count - 1)
                samples.AddRange(new float[(int)(plan[i].PauseAfter * PocketEngine.SampleRate)]);
            onPiece?.Invoke(i + 1, plan.Count);
        }
        return [.. samples];
    }

    public static void WriteWav(string path, float[] samples)
    {
        var temp = path + ".part";
        File.WriteAllBytes(temp, PcmWav.WrapPcmToWav(PcmWav.Pcm16FromFloat(samples), PocketEngine.SampleRate));
        File.Move(temp, path, overwrite: true);
    }
}
