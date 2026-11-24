using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Hearwood.Pocket;
using Hearwood.Text;

namespace Hearwood;

/// <summary>
/// Plays a <see cref="SpeechStream"/> on the default device: speak, stop, pause, and the word being heard.
/// Each utterance has its own stream and device, so a stopped one can never touch the next.
/// </summary>
public sealed class SpeechPlayer : IDisposable
{
    readonly PocketService _pocket;
    Utterance? _current;

    public SpeechPlayer(PocketService pocket) => _pocket = pocket;

    /// <summary>Playback speed; the stream reads it every 20 ms.</summary>
    public double Rate { get; set; } = 1.0;

    /// <summary>A message for the user (raised on a worker thread).</summary>
    public event Action<string>? Error;

    /// <summary>Speaking, paused or finished changed (raised on any thread).</summary>
    public event Action? StateChanged;

    public bool IsSpeaking => _current is { Finished: false };
    public bool IsPaused => _current is { Finished: false, Paused: true };

    /// <summary>What the utterance was started with, so the UI knows whether the words reported are the text box's.</summary>
    public object? CurrentTag => _current is { Finished: false } u ? u.Tag : null;

    /// <summary>The user's word substitutions, applied to what each utterance says from when it starts.</summary>
    public Substitutions Substitutions { get; set; } = Substitutions.Empty;

    /// <summary>
    /// Speaks the <paramref name="part"/> of <paramref name="text"/> (all of it by default), with the word substitutions
    /// unless <paramref name="substitute"/> is false; the words reported are offsets in the whole text.
    /// </summary>
    public void Speak(string text, string voice, object? tag = null, Range? part = null, bool substitute = true)
    {
        Stop();
        var plan = SpeechPlan.Build(text, part ?? Range.All, substitute ? Substitutions.Apply : null);
        if (plan.Count == 0)
            return;
        var stream = new SpeechStream(_ => _pocket.ReadyAsync(voice), plan, voice) { Rate = () => Rate };
        var u = new Utterance(stream, tag);
        stream.Failed += e => Fail(u, e.Message);
        u.Device.PlaybackStopped += (_, e) =>
        {
            u.Finished = true;
            if (e.Exception is not null) Fail(u, "Playback failed: " + e.Exception.Message);
            else if (ReferenceEquals(_current, u)) StateChanged?.Invoke();
        };
        _current = u;
        stream.Start();
        u.Device.Init(new SampleToWaveProvider16(stream));
        u.Device.Play();
        StateChanged?.Invoke();
    }

    public void Stop()
    {
        var u = Interlocked.Exchange(ref _current, null);
        if (u is null)
            return;
        u.Dispose();
        StateChanged?.Invoke();
    }

    public void TogglePause()
    {
        if (_current is not { Finished: false } u)
            return;
        u.Paused = !u.Paused;
        if (u.Paused) u.Device.Pause();
        else u.Device.Play();
        StateChanged?.Invoke();
    }

    /// <summary>The source span of the word being heard, or null between utterances.</summary>
    public WordSpan? CurrentWord()
    {
        if (_current is not { Finished: false } u)
            return null;
        // Samples the device has played (16-bit mono).
        return u.Stream.WordAt(u.Device.GetPosition() / 2);
    }

    void Fail(Utterance u, string message)
    {
        if (!ReferenceEquals(_current, u))
            return;
        Stop();
        Error?.Invoke(message);
    }

    public void Dispose() => Stop();

    sealed class Utterance(SpeechStream stream, object? tag) : IDisposable
    {
        public SpeechStream Stream { get; } = stream;
        public WaveOut Device { get; } = new() { BufferMilliseconds = 50, NumberOfBuffers = 3 };
        public object? Tag { get; } = tag;
        public volatile bool Finished;
        public volatile bool Paused;

        public void Dispose()
        {
            Finished = true;
            Stream.Dispose();
            try { Device.Stop(); } catch (Exception) { }
            Device.Dispose();
        }
    }
}
