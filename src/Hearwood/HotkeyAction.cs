namespace Hearwood;

/// <summary>What a global hotkey does. Each has a default combination the user can change from the tray.</summary>
public enum HotkeyAction { Read, Pause, Stop, Faster, Slower }

public static class HotkeyActions
{
    public static IReadOnlyList<HotkeyAction> All { get; } = Enum.GetValues<HotkeyAction>();

    public static Hotkey Default(this HotkeyAction action) => new(Keys.Control | Keys.Alt, action switch
    {
        HotkeyAction.Read => Keys.S,
        HotkeyAction.Pause => Keys.P,
        HotkeyAction.Stop => Keys.X,
        HotkeyAction.Faster => Keys.Up,
        HotkeyAction.Slower => Keys.Down,
        _ => throw new ArgumentOutOfRangeException(nameof(action)),
    });

    /// <summary>For menus and titles: "Read selection".</summary>
    public static string Name(this HotkeyAction action) => action switch
    {
        HotkeyAction.Read => "Read selection",
        HotkeyAction.Pause => "Pause / resume",
        HotkeyAction.Stop => "Stop",
        HotkeyAction.Faster => "Faster",
        HotkeyAction.Slower => "Slower",
        _ => throw new ArgumentOutOfRangeException(nameof(action)),
    };

    /// <summary>Finishes "Press the keys that should …".</summary>
    public static string Does(this HotkeyAction action) => action switch
    {
        HotkeyAction.Read => "read selected text",
        HotkeyAction.Pause => "pause or resume speech",
        HotkeyAction.Stop => "stop speaking",
        HotkeyAction.Faster => "speed speech up",
        HotkeyAction.Slower => "slow speech down",
        _ => throw new ArgumentOutOfRangeException(nameof(action)),
    };
}
