namespace Hearwood.Pocket;

/// <summary>
/// The app's one Pocket engine: getting the model in place on first run, the voice library, and the
/// engine's lifetime. <see cref="ReadyAsync"/> starts loading at most once and can be awaited from anywhere.
/// <para>
/// The Store package ships the model ready to use next to the exe (<see cref="BundledCheckpointDir"/>, written by
/// tools/build-msix.ps1). That folder is read-only, so when it is complete it is used as it is: nothing is
/// downloaded, converted or copied into it. Other builds download the model into <see cref="DataDir"/>.
/// </para>
/// </summary>
public sealed class PocketService : IDisposable
{
    /// <summary>The voice chosen when no other is.</summary>
    public const string DefaultVoice = "Laurel";

    public static string DataDir => Path.Combine(Config.Folder, "pocket");

    /// <summary>The model folder the Store package ships: converted, with the voices in it.</summary>
    public static string BundledCheckpointDir => Path.Combine(AppContext.BaseDirectory, "pocket", "kyutai-" + PocketSetup.Checkpoint);

    readonly object _gate = new();
    readonly PocketSetup _setup;
    /// <summary>The model came with the app, so it is used as it is.</summary>
    readonly bool _bundled;
    Task<PocketEngine>? _ready;

    /// <param name="dataDir">Where to download the model; when given, a bundled model is ignored (for tests).</param>
    public PocketService(string? dataDir = null)
    {
        _bundled = dataDir is null && IsCompleteBundle(BundledCheckpointDir);
        var checkpointDir = _bundled ? BundledCheckpointDir : Path.Combine(dataDir ?? DataDir, "kyutai-" + PocketSetup.Checkpoint);
        _setup = new PocketSetup(checkpointDir);
        Voices = new VoiceLibrary(checkpointDir);
    }

    /// <summary>Every model file and every built-in voice is in <paramref name="checkpointDir"/>.</summary>
    public static bool IsCompleteBundle(string checkpointDir) =>
        PocketEngine.FilesIn(checkpointDir).All(File.Exists) &&
        VoiceLibrary.BuiltIn.Keys.All(v => File.Exists(PocketEngine.VoiceFile(checkpointDir, v)));

    public VoiceLibrary Voices { get; }

    /// <summary>Status lines while the model is copied or downloaded (raised on a worker thread).</summary>
    public event Action<string, float>? Progress;

    public bool IsReady => _ready is { IsCompletedSuccessfully: true };

    /// <summary>
    /// The engine, once the model is in place and loaded with <paramref name="warmVoice"/>. A failed attempt
    /// is forgotten, so the next call tries again (after a dropped connection, say).
    /// </summary>
    public Task<PocketEngine> ReadyAsync(string? warmVoice = null)
    {
        lock (_gate)
        {
            if (_ready is null || _ready.IsFaulted || _ready.IsCanceled)
                _ready = Task.Run(() => LoadAsync(warmVoice ?? DefaultVoice));
            return _ready;
        }
    }

    async Task<PocketEngine> LoadAsync(string warmVoice)
    {
        if (PocketEngine.WhyUnavailable() is { } why)
            throw new InvalidOperationException(why);

        if (!_bundled)
        {
            await _setup.EnsureAsync((status, p) => Progress?.Invoke(status, p));
            Voices.InstallBundled();
        }

        var engine = new PocketEngine(_setup.CheckpointDir);
        engine.Warm(Voices.Exists(warmVoice) ? warmVoice : DefaultVoice);
        return engine;
    }

    public void Dispose()
    {
        if (IsReady)
            _ready!.Result.Dispose();
    }
}
