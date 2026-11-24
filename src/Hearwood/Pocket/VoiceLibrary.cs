namespace Hearwood.Pocket;

/// <summary>
/// The voices that ship with the app: ready-made ptts voice files (<c>assets/voices/&lt;name&gt;.safetensors</c>,
/// next to the exe), copied into the checkpoint's <c>voices\</c> folder, where ptts reads them when it loads.
/// Each is a blend of two Kokoro-82M voices, spoken by Kokoro and cloned once into Pocket; the recipes are
/// in <c>voices/Voice mixes.txt</c> in the repo.
/// </summary>
public sealed class VoiceLibrary(string checkpointDir)
{
    /// <summary>The bundled voice files, next to the exe.</summary>
    public static string BundledFolder => Path.Combine(AppContext.BaseDirectory, "assets", "voices");

    /// <summary>Every voice, in menu order, with how it sounds.</summary>
    public static IReadOnlyDictionary<string, string> BuiltIn { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Willow"] = "American, female",
        ["Hazel"] = "American, female",
        ["Juniper"] = "American, female",
        ["Laurel"] = "American, female",
        ["Ivy"] = "American, female",
        ["Aspen"] = "British, female",
        ["Heather"] = "British, female",
        ["Primrose"] = "British, female",
        ["Alder"] = "American, male",
        ["Rowan"] = "American, male",
        ["Cedar"] = "American, male",
        ["Elm"] = "American, male",
        ["Linden"] = "British, male",
        ["Hawthorn"] = "British, male",
        ["Birch"] = "British, male",
    };

    public IReadOnlyList<string> List() => [.. BuiltIn.Keys];

    public bool Exists(string name) => BuiltIn.ContainsKey(name);

    /// <summary>The name shown in menus, with how the voice sounds ("Willow – American, female").</summary>
    public static string Label(string name) => BuiltIn.TryGetValue(name, out var sound) ? $"{name} – {sound}" : name;

    /// <summary>
    /// Puts each bundled voice file in the checkpoint's voices folder, replacing a copy that differs. Must run
    /// before the engine loads, because ptts only reads the folder then.
    /// </summary>
    public void InstallBundled(string? bundledFolder = null)
    {
        var source = bundledFolder ?? BundledFolder;
        foreach (var name in BuiltIn.Keys)
        {
            var from = Path.Combine(source, name + ".safetensors");
            if (!File.Exists(from))
                throw new FileNotFoundException($"The voice file for '{name}' is missing from '{source}'. Reinstall Hearwood.");
            var target = PocketEngine.VoiceFile(checkpointDir, name);
            if (File.Exists(target) && File.ReadAllBytes(target).AsSpan().SequenceEqual(File.ReadAllBytes(from)))
                continue;
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(from, target + ".part", overwrite: true);
            File.Move(target + ".part", target, overwrite: true);
        }
    }
}
