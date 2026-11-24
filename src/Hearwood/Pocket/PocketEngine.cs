using System.Runtime.InteropServices;
using Hearwood.Audio;

namespace Hearwood.Pocket;

/// <summary>
/// Kyutai's Pocket TTS 100M run by Gradium's Phonon runtime: ptts's C library (<c>ptts_ffi</c>, built by
/// tools/build-phonon.ps1) with its inference engine and English text normaliser. CPU only. Ported from
/// another project by the same author, but speaking one piece per call so playback can start after the first sentence.
/// <para>
/// ptts reads its voices from the checkpoint's <c>voices\</c> when it loads; <see cref="VoiceLibrary.InstallBundled"/>
/// puts the app's voice files there first.
/// </para>
/// </summary>
public sealed class PocketEngine(string checkpointDir) : IDisposable
{
    /// <summary>On 32 hardware threads 4 to 16 run at about the same speed (7–8× real time), 2 slower; 4 also suits smaller CPUs.</summary>
    private const int MaxThreads = 4;

    public const int SampleRate = 24000;

    /// <summary>The checkpoint is English; the code also picks ptts's text normalisation.</summary>
    public const string Language = "en";

    /// <summary>english_2026-10's sampling temperature (Kyutai: "human evals preferred this model at 0.3").</summary>
    public const float Temperature = 0.3f;

    /// <summary>Any fixed value: the same seed and text give the same audio on the same device and model.</summary>
    public const ulong Seed = 0;

    /// <summary>
    /// ptts starts the audio decoder from a zero state on every call, and its first 13–20 ms are a tick about
    /// 40 dB below the speech. That is above <see cref="SilenceTrim"/>'s 60 dB floor, so the trim kept the
    /// 0.5–0.8 s of silence the model leads with. 40 ms is twice the longest tick (<see cref="Finish"/>).
    /// </summary>
    public const int DecoderWarmUp = SampleRate * 40 / 1000;

    public const string WeightsFile = "model.safetensors";
    public const string BosFile = "bos_before_voice.npy";
    public const string ConfigFile = "config.json";
    public const string TokenizerFile = "tokenizer.json";

    private readonly object _gate = new();
    private nint _handle;
    private HashSet<string> _loaded = [];
    private string? _applied;

    /// <summary>Everything that must be in the checkpoint folder before an engine is built.</summary>
    public static IEnumerable<string> FilesIn(string checkpointDir) =>
        new[] { ConfigFile, TokenizerFile, WeightsFile, BosFile }.Select(f => Path.Combine(checkpointDir, f));

    /// <summary>Why the library cannot run here, or null. Checked first: on a CPU without AVX2 it crashes the whole app rather than failing.</summary>
    public static string? WhyUnavailable(bool libraryPresent, Architecture architecture, bool hasRequiredCpuFeatures)
    {
        if (!libraryPresent)
            return $"Pocket is not included in this build ({PttsNative.LibraryFile} is missing). Run tools/build-phonon.ps1.";
        if (architecture == Architecture.X64 && !hasRequiredCpuFeatures)
            return "Pocket needs a CPU with AVX2, which this one does not have.";
        return null;
    }

    /// <summary><see cref="WhyUnavailable(bool, Architecture, bool)"/> for this process.</summary>
    public static string? WhyUnavailable() => WhyUnavailable(
        File.Exists(Path.Combine(AppContext.BaseDirectory, PttsNative.LibraryFile)),
        RuntimeInformation.ProcessArchitecture,
        System.Runtime.Intrinsics.X86.Avx2.IsSupported && System.Runtime.Intrinsics.X86.Fma.IsSupported);

    /// <summary>Where ptts finds the voices: <c>&lt;checkpoint&gt;\voices\&lt;voice&gt;.safetensors</c>.</summary>
    public static string VoiceFile(string checkpointDir, string voice) => Path.Combine(checkpointDir, "voices", voice + ".safetensors");

    /// <summary>Loads the library with this voice ready, so the first sentence does not wait for it.</summary>
    public void Warm(string voice, CancellationToken cancellationToken = default)
    {
        lock (_gate)
            Prepare(voice, cancellationToken);
    }

    /// <summary>
    /// The speech for one piece of text (at most <see cref="Text.SpeechPlan.MaxPieceLength"/> characters),
    /// its quiet ends trimmed. Throws <see cref="OperationCanceledException"/> when cancelled mid-piece.
    /// </summary>
    public float[] Speak(string text, string voice, CancellationToken cancellationToken)
    {
        // The library takes one call at a time on a handle.
        lock (_gate)
        {
            Prepare(voice, cancellationToken);

            var audio = new List<float>(SampleRate * 4);
            PttsNative.FrameCallback onFrames = (pcm, count, _) =>
            {
                if (cancellationToken.IsCancellationRequested)
                    return false; // Exceptions must not cross into native code.
                var frames = new float[(int)count];
                Marshal.Copy(pcm, frames, 0, frames.Length);
                audio.AddRange(frames);
                return true;
            };

            bool ok = PttsNative.ptts_speak(_handle, text, onFrames, 0, out _);
            GC.KeepAlive(onFrames);

            cancellationToken.ThrowIfCancellationRequested();
            if (!ok)
                throw new InvalidOperationException($"Pocket could not read “{text}”: {PttsNative.LastError(_handle)}");
            return Finish([.. audio]);
        }
    }

    /// <summary>
    /// A call's audio, trimmed, without the decoder's warm-up (<see cref="DecoderWarmUp"/>) when silence follows
    /// it, so the trim can cut the model's leading silence. About a quarter of calls (short pieces, numbers,
    /// "I.", "Kick it.") speak within their first 20 ms; they are left whole, since cutting them clipped the word
    /// and started the piece on a step. As in the code it was ported from.
    /// </summary>
    internal static float[] Finish(float[] audio)
    {
        if (audio.Length > DecoderWarmUp)
        {
            var (rest, head) = SilenceTrim.TrimSilence(audio[DecoderWarmUp..]);
            if (head > 0)
                return rest;
        }
        return SilenceTrim.TrimSilence(audio).Trimmed;
    }

    private void Prepare(string voice, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_handle == 0)
            Load();
        if (!_loaded.Contains(voice))
            throw new InvalidOperationException($"Pocket has no voice '{voice}' in '{Path.GetDirectoryName(VoiceFile(checkpointDir, voice))}'.");
        if (_applied == voice)
            return;

        if (!PttsNative.ptts_set_voice(_handle, voice))
            throw new InvalidOperationException($"Pocket has no voice '{voice}': {PttsNative.LastError(_handle)}");
        if (!PttsNative.ptts_set_temperature(_handle, Temperature))
            throw new InvalidOperationException($"Pocket refused temperature {Temperature}: {PttsNative.LastError(_handle)}");
        PttsNative.ptts_set_seed(_handle, Seed);
        _applied = voice;
    }

    private void Load()
    {
        Free();

        // Read once, when the library's thread pool starts, so it must be set before the first load.
        if (Environment.GetEnvironmentVariable("RAYON_NUM_THREADS") is null)
            Environment.SetEnvironmentVariable("RAYON_NUM_THREADS", Math.Min(Environment.ProcessorCount, MaxThreads).ToString());

        var handle = PttsNative.ptts_new(checkpointDir, PttsNative.CpuUnit, Language);
        if (handle == 0)
            throw new InvalidOperationException($"Pocket could not load its model: {PttsNative.LastError(0)}");
        _handle = handle;
        _loaded = [.. PttsNative.ReadNames(PttsNative.ptts_voices(handle))];
    }

    private void Free()
    {
        if (_handle != 0)
            PttsNative.ptts_free(_handle);
        _handle = 0;
        _loaded = [];
        _applied = null;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            Free();
        }
    }
}
