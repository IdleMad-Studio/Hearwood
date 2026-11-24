namespace Hearwood;

/// <summary>
/// A global hotkey: Ctrl and/or Alt, optionally Shift, with one other key. Written as "Ctrl+Alt+S",
/// which is both what the user sees and what the config stores.
/// </summary>
public readonly record struct Hotkey(Keys Modifiers, Keys Key)
{
    /// <summary>Names for keys whose <see cref="Keys"/> name is unclear ("D1") or shared by two members (PageUp/Prior).</summary>
    static readonly Dictionary<Keys, string> Names = Enumerable.Range(0, 10)
        .ToDictionary(i => Keys.D0 + i, i => i.ToString())
        .Concat(new Dictionary<Keys, string>
        {
            [Keys.PageUp] = "PageUp", [Keys.PageDown] = "PageDown", [Keys.Enter] = "Enter", [Keys.CapsLock] = "CapsLock",
        })
        .ToDictionary();

    /// <summary>
    /// Ctrl or Alt is required: Shift alone, or no modifier, would take a key the user types with. The key must be
    /// a real key, not a modifier.
    /// </summary>
    public bool IsValid =>
        (Modifiers & (Keys.Control | Keys.Alt)) != 0 &&
        (Modifiers & ~(Keys.Control | Keys.Alt | Keys.Shift)) == 0 &&
        Key != Keys.None && (Key & Keys.Modifiers) == 0 && Enum.IsDefined(Key) && !IsModifierKey(Key);

    /// <summary>The modifiers as RegisterHotKey takes them.</summary>
    public uint NativeModifiers =>
        ((Modifiers & Keys.Control) != 0 ? Native.MOD_CONTROL : 0) |
        ((Modifiers & Keys.Alt) != 0 ? Native.MOD_ALT : 0) |
        ((Modifiers & Keys.Shift) != 0 ? Native.MOD_SHIFT : 0);

    public static bool IsModifierKey(Keys key) => key is
        Keys.ControlKey or Keys.LControlKey or Keys.RControlKey or Keys.Menu or Keys.LMenu or Keys.RMenu or
        Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey or Keys.LWin or Keys.RWin;

    /// <summary>"Ctrl+Alt+" and so on, for showing the modifiers held before the key is pressed.</summary>
    public static string ModifierText(Keys modifiers) =>
        ((modifiers & Keys.Control) != 0 ? "Ctrl+" : "") +
        ((modifiers & Keys.Alt) != 0 ? "Alt+" : "") +
        ((modifiers & Keys.Shift) != 0 ? "Shift+" : "");

    public override string ToString() => ModifierText(Modifiers) + (Names.TryGetValue(Key, out var name) ? name : Key.ToString());

    /// <summary>Reads text written by <see cref="ToString"/>; true only for a valid hotkey.</summary>
    public static bool TryParse(string? text, out Hotkey hotkey)
    {
        hotkey = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var parts = text.Split('+', StringSplitOptions.TrimEntries);

        var modifiers = Keys.None;
        foreach (var part in parts[..^1])
        {
            var m = part.ToLowerInvariant() switch
            {
                "ctrl" or "control" => Keys.Control, "alt" => Keys.Alt, "shift" => Keys.Shift, _ => Keys.None,
            };
            if (m == Keys.None) return false;
            modifiers |= m;
        }

        var keyName = parts[^1];
        var named = Names.Where(n => string.Equals(n.Value, keyName, StringComparison.OrdinalIgnoreCase)).Select(n => n.Key).ToList();
        Keys key;
        if (named.Count > 0) key = named[0];
        // Letters only: Enum.TryParse also takes numbers and comma-separated lists of members.
        else if (keyName.Length == 0 || !keyName.All(char.IsLetterOrDigit) || !char.IsLetter(keyName[0])
                 || !Enum.TryParse(keyName, ignoreCase: true, out key)) return false;

        hotkey = new Hotkey(modifiers, key);
        return hotkey.IsValid;
    }
}
