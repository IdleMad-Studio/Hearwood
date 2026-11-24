using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hearwood;

public sealed class Config
{
    public const double MinRate = 0.5;
    public const double MaxRate = 3.0;

    /// <summary>The speeds offered, as in the voice preview of another app by the same author.</summary>
    public static IReadOnlyList<double> SpeedChoices { get; } = [0.75, 1.0, 1.25, 1.5, 1.75, 2.0, 2.25, 2.5, 2.75, 3.0];

    /// <summary>%APPDATA%\Hearwood: the settings and the Pocket model and voices.</summary>
    public static string Folder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Hearwood");

    static string PathOnDisk => Path.Combine(Folder, "config.json");

    /// <summary>Indented, and with "+" left as it is, so a hotkey reads "Ctrl+Alt+S" in the file rather than "Ctrl+Alt+S".</summary>
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public string? Voice { get; set; }
    public double Rate { get; set; } = 1.0;
    public bool StartWithWindows { get; set; }

    /// <summary>
    /// Each action's hotkey as text, keyed by action name: "Read": "Ctrl+Alt+S". Use <see cref="GetHotkey"/>, which
    /// gives the default for an entry that is missing or can't be read.
    /// </summary>
    [JsonPropertyName("Hotkeys")]
    public Dictionary<string, string>? HotkeyTexts { get; set; } =
        HotkeyActions.All.ToDictionary(a => a.ToString(), a => a.Default().ToString());

    public Hotkey GetHotkey(HotkeyAction action) =>
        HotkeyTexts?.GetValueOrDefault(action.ToString()) is { } text && Hotkey.TryParse(text, out var h) ? h : action.Default();

    public void SetHotkey(HotkeyAction action, Hotkey hotkey) =>
        (HotkeyTexts ??= new())[action.ToString()] = hotkey.ToString();

    /// <summary>The listed speed nearest to <paramref name="rate"/> (an older config may hold any value).</summary>
    public static double SnapRate(double rate) => SpeedChoices.MinBy(c => Math.Abs(c - rate));

    /// <summary>The listed speed <paramref name="steps"/> places from <paramref name="rate"/>, stopping at the ends.</summary>
    public static double StepRate(double rate, int steps)
    {
        int at = SpeedChoices.ToList().IndexOf(SnapRate(rate));
        return SpeedChoices[Math.Clamp(at + steps, 0, SpeedChoices.Count - 1)];
    }

    public static Config Load()
    {
        try
        {
            if (File.Exists(PathOnDisk))
            {
                var c = JsonSerializer.Deserialize<Config>(File.ReadAllText(PathOnDisk));
                if (c != null) { c.Rate = SnapRate(c.Rate); return c; }
            }
        }
        catch (Exception) { /* corrupt file: fall back to defaults */ }
        return new Config();
    }

    public void Save()
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(PathOnDisk, JsonSerializer.Serialize(this, JsonOptions));
    }
}
