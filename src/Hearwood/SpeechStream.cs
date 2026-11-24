using System.Collections.Concurrent;
using NAudio.Wave;
using Hearwood.Pocket;
using Hearwood.Text;

namespace Hearwood;

/// <summary>
/// One run of speech as a sample stream: a worker synthesizes the plan's pieces a few ahead, and
/// <see cref="Read"/> passes them through <see cref="Audio.Sonic"/> in 20 ms blocks, so a new <see cref="Rate"/> is heard
/// almost at once (as in another app by the same author) while the pitch stays put. It records which source sample
/// each output sample came from, through Sonic's buffering, so the word being heard can be found.
/// </summary>
public sealed class SpeechStream : ISampleProvider, IDisposable
{
    /// <summary>Pieces synthesized ahead of the one playing. Pocket runs several times faster than real time.</summary>
    const int Lookahead = 3;

    /// <summary>Source samples fed to Sonic at a time: 20 ms, so a new speed applies almost at once.</summary>
    const int Block = PocketEngine.SampleRate / 50;

    /// <summary>
    /// A piece's speech followed by its pause, placed at <see cref="Start"/> in the stream of source samples.
    /// Words are timed over its audible part, <see cref="VoiceStart"/> for <see cref="VoiceLength"/> samples.
    /// </summary>
    sealed record Segment(int Piece, float[] Samples, long Start, int VoiceStart, int VoiceLength);

    /// <summary>
    /// Speech quieter than this, relative to the piece's loudest frame, is not yet (or no longer) a word.
    /// The engine's own trim, at 60 dB, keeps a soft lead-in and tail that would light words too early.
    /// </summary>
    const double AudibleDb = 35;

    readonly Func<CancellationToken, Task<PocketEngine>> _engine;
    readonly IReadOnlyList<SpeechPiece> _plan;
    readonly string _voice;
    readonly bool _waitForSpeech;
    readonly CancellationTokenSource _cts = new();
    readonly BlockingCollection<Segment> _ready = new(Lookahead);
    readonly Audio.Sonic _stretch = new(PocketEngine.SampleRate);
    readonly object _marksGate = new();
    // (output samples produced, source sample reached at that point), appended as blocks are produced.
    readonly List<(long Out, double Source)> _marks = [(0, 0)];
    readonly List<Segment> _segments = [];
    readonly float[] _feed = new float[Block];

    Segment? _playing;
    int _offset;
    long _fed;
    long _produced;
    bool _flushed;

    /// <param name="waitForSpeech">
    /// Block in <see cref="Read"/> until the next piece is ready instead of filling the gap with silence:
    /// for rendering to a file, never for a device, whose thread must not stall.
    /// </param>
    public SpeechStream(Func<CancellationToken, Task<PocketEngine>> engine, IReadOnlyList<SpeechPiece> plan, string voice, bool waitForSpeech = false)
    {
        _engine = engine;
        _plan = plan;
        _voice = voice;
        _waitForSpeech = waitForSpeech;
    }

    /// <summary>Playback speed; read for every 20 ms block.</summary>
    public Func<double> Rate { get; init; } = () => 1.0;

    /// <summary>Raised once, on the worker, when a piece cannot be synthesized.</summary>
    public event Action<Exception>? Failed;

    public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(PocketEngine.SampleRate, 1);

    public void Start() => _ = Task.Run(ProduceAsync);

    async Task ProduceAsync()
    {
        var ct = _cts.Token;
        try
        {
            var engine = await _engine(ct).WaitAsync(ct);
            long start = 0;
            for (int i = 0; i < _plan.Count; i++)
            {
                var speech = engine.Speak(_plan[i].Text, _voice, ct);
                var samples = new float[speech.Length + (int)(_plan[i].PauseAfter * PocketEngine.SampleRate)];
                speech.CopyTo(samples, 0);
                var (voiced, head) = Audio.SilenceTrim.TrimSilence(speech, topDb: AudibleDb);
                _ready.Add(new Segment(i, samples, start, head, voiced.Length), ct);
                start += samples.Length;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            Failed?.Invoke(e);
        }
        finally
        {
            _ready.CompleteAdding();
        }
    }

    /// <summary>Stretched audio; silence while the next piece is still being made (unless waiting); 0 at the end.</summary>
    public int Read(Span<float> buffer)
    {
        int count = buffer.Length;
        int written = 0;
        while (written < count)
        {
            if (_stretch.AvailableSamples > 0)
            {
                written += _stretch.Read(buffer[written..]);
                continue;
            }
            if (_flushed)
                break;
            if (FeedBlock())
                continue;
            if (_ready.IsCompleted)
            {
                // Out of source: push Sonic's last samples out.
                _stretch.Flush();
                _flushed = true;
                continue;
            }
            // Underrun: the next piece is not ready yet. Silence, which is not part of the source.
            buffer[written..].Clear();
            written = count;
        }
        Mark(written);
        return written;
    }

    /// <summary>Puts the next 20 ms of source into Sonic at the current speed; false when no source is ready.</summary>
    bool FeedBlock()
    {
        if (_playing is null || _offset >= _playing.Samples.Length)
        {
            _playing = null;
            Segment? next;
            try
            {
                if (_waitForSpeech ? !_ready.TryTake(out next, Timeout.Infinite, _cts.Token) : !_ready.TryTake(out next))
                    return false;
            }
            catch (OperationCanceledException) { return false; }
            _playing = next;
            _offset = 0;
            lock (_marksGate)
                _segments.Add(next);
        }
        int n = Math.Min(Block, _playing.Samples.Length - _offset);
        Array.Copy(_playing.Samples, _offset, _feed, 0, n);
        _offset += n;
        _stretch.Speed = Math.Clamp(Rate(), Config.MinRate, Config.MaxRate);
        _stretch.Write(_feed.AsSpan(0, n));
        _fed += n;
        return true;
    }

    /// <summary>Records which source sample the end of what was just produced corresponds to.</summary>
    void Mark(int produced)
    {
        _produced += produced;
        // Still inside Sonic: input not yet processed, and output not yet taken (worth speed× as much source).
        double source = _fed - _stretch.UnprocessedSamples - _stretch.AvailableSamples * _stretch.Speed;
        lock (_marksGate)
        {
            _marks.Add((_produced, Math.Max(_marks[^1].Source, source)));
            if (_marks.Count > 4096)
                _marks.RemoveRange(0, 2048);
        }
    }

    /// <summary>The source span of the word being heard once <paramref name="outputSamples"/> have been played.</summary>
    public WordSpan? WordAt(long outputSamples)
    {
        lock (_marksGate)
        {
            double source = SourceAt(outputSamples);
            int at = _segments.Count - 1;
            while (at >= 0 && _segments[at].Start > source)
                at--;
            if (at < 0)
                return null;
            var segment = _segments[at];
            // In a piece's quiet lead-in the previous piece's last word stays lit.
            if (at > 0 && source < segment.Start + segment.VoiceStart && _plan[_segments[at - 1].Piece].Words is { Count: > 0 } before)
                return before[^1];
            // Before the voice starts the first word is lit, and after it ends (in the pause) the last stays lit.
            double into = Math.Clamp(source - segment.Start - segment.VoiceStart, 0, segment.VoiceLength);
            return SpeechPlan.WordAt(_plan[segment.Piece].Words, into, segment.VoiceLength);
        }
    }

    double SourceAt(long output)
    {
        // Linear between the two marks around it.
        int hi = _marks.FindIndex(m => m.Out >= output);
        if (hi < 0)
            return _marks[^1].Source;
        if (hi == 0)
            return _marks[0].Source;
        var (o0, s0) = _marks[hi - 1];
        var (o1, s1) = _marks[hi];
        return o1 == o0 ? s1 : s0 + (s1 - s0) * (output - o0) / (o1 - o0);
    }

    public void Dispose() => _cts.Cancel();
}
