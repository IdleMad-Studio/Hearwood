using Hearwood.Pocket;

namespace Hearwood;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
#if DEBUG
        // Engine checks for development. Release builds cannot write audio files.
        if (args.Length > 0 && args[0] == "--speak-to")
            return SpeakTo(args[1..]);
        if (args.Length > 0 && args[0] == "--stream-to")
            return StreamTo(args[1..]);
#endif

        using var showRequests = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\Hearwood.Show");
        using var mutex = new Mutex(true, @"Local\Hearwood.SingleInstance", out bool first);
        if (!first)
        {
            // Already running: bring its window up instead.
            showRequests.Set();
            return 0;
        }

        ApplicationConfiguration.Initialize();
        // HEARWOOD_THEME=light|dark overrides Windows' app mode, for checking both palettes.
        Application.SetColorMode(Environment.GetEnvironmentVariable("HEARWOOD_THEME")?.ToLowerInvariant() switch
        {
            "light" => SystemColorMode.Classic,
            "dark" => SystemColorMode.Dark,
            _ => SystemColorMode.System,
        });
        // Started with Windows (--tray, or the Store package's startup task): only the tray icon. Started by hand: the window too.
        bool atSignIn = args.Contains("--tray") || StartupRegistration.StartedAtSignIn();
        Application.Run(new TrayApp(showWindow: !atSignIn, showRequests));
        return 0;
    }

#if DEBUG
    /// <summary>
    /// <c>Hearwood.exe --speak-to OUT.wav [--voice NAME] TEXT...</c>: writes TEXT as a 1× WAV and exits, for
    /// checking the engine without the UI. Exit code 0 on success; errors go to OUT.wav.error.txt.
    /// </summary>
    static int SpeakTo(string[] args)
    {
        var output = args.Length > 0 ? args[0] : "speech.wav";
        try
        {
            string voice = PocketService.DefaultVoice;
            var rest = args.Skip(1).ToList();
            if (rest.Count >= 2 && rest[0] == "--voice")
            {
                voice = rest[1];
                rest = rest[2..];
            }
            using var pocket = new PocketService();
            var engine = pocket.ReadyAsync(voice).GetAwaiter().GetResult();
            SpeechRender.WriteWav(output, SpeechRender.Render(engine, string.Join(' ', rest), voice, CancellationToken.None));
            return 0;
        }
        catch (Exception e)
        {
            File.WriteAllText(output + ".error.txt", e.ToString());
            return 1;
        }
    }

    /// <summary>
    /// <c>Hearwood.exe --stream-to OUT.wav RATE TEXT...</c>: plays TEXT through the same stretched stream as
    /// the speakers get, into a WAV, and writes OUT.wav.words.txt with the time each word would be lit.
    /// </summary>
    static int StreamTo(string[] args)
    {
        var output = args.Length > 0 ? args[0] : "stream.wav";
        try
        {
            double rate = double.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture);
            var text = string.Join(' ', args[2..]);
            using var pocket = new PocketService();
            string voice = PocketService.DefaultVoice;
            using var stream = new SpeechStream(_ => pocket.ReadyAsync(voice), Text.SpeechPlan.Build(text), voice, waitForSpeech: true) { Rate = () => rate };
            stream.Start();

            var samples = new List<float>();
            var log = new System.Text.StringBuilder();
            var buffer = new float[PocketEngine.SampleRate / 100]; // 10 ms, as a device would pull
            Text.WordSpan? lit = null;
            int n;
            while ((n = stream.Read(buffer)) > 0)
            {
                samples.AddRange(buffer.AsSpan(0, n));
                var word = stream.WordAt(samples.Count);
                if (word != lit && word is { } w)
                    log.AppendLine($"{samples.Count / (double)PocketEngine.SampleRate:0.00}\t{text.Substring(w.Start, w.Length)}");
                lit = word;
            }
            SpeechRender.WriteWav(output, [.. samples]);
            File.WriteAllText(output + ".words.txt", log.ToString());
            return 0;
        }
        catch (Exception e)
        {
            File.WriteAllText(output + ".error.txt", e.ToString());
            return 1;
        }
    }
#endif
}
