namespace Hearwood.Ui;

/// <summary>The window's palette: cedar greens on warm neutrals, in a light and a dark version that follow Windows.</summary>
public sealed record Theme(
    Color Window, Color Surface, Color Control, Color ControlHover, Color ControlPressed, Color Border,
    Color Text, Color Muted, Color Accent, Color AccentHover, Color AccentPressed, Color OnAccent, Color Highlight)
{
    public static readonly Theme Light = new(
        Window: Hex(0xF3F5F2), Surface: Hex(0xFFFFFF), Control: Hex(0xE6EBE7), ControlHover: Hex(0xDAE2DC), ControlPressed: Hex(0xCBD6CE),
        Border: Hex(0xD9E0DA), Text: Hex(0x1C2421), Muted: Hex(0x66726D),
        Accent: Hex(0x1F7A66), AccentHover: Hex(0x238A73), AccentPressed: Hex(0x18604F), OnAccent: Hex(0xFFFFFF),
        Highlight: Hex(0xC9EBDF));

    public static readonly Theme Dark = new(
        Window: Hex(0x141917), Surface: Hex(0x1D2321), Control: Hex(0x29312E), ControlHover: Hex(0x323C38), ControlPressed: Hex(0x3B4742),
        Border: Hex(0x2E3834), Text: Hex(0xE4EBE7), Muted: Hex(0x93A19B),
        Accent: Hex(0x3FB497), AccentHover: Hex(0x4CC4A6), AccentPressed: Hex(0x34987F), OnAccent: Hex(0x0E1513),
        Highlight: Hex(0x245246));

    /// <summary>The palette for the Windows app mode the app started in.</summary>
    public static Theme Current => Application.IsDarkModeEnabled ? Dark : Light;

    /// <summary>Segoe UI Variable on Windows 11, Segoe UI before it.</summary>
    public static Font UiFont(float size, FontStyle style = FontStyle.Regular) =>
        new(FontFamily.Families.Any(f => f.Name == "Segoe UI Variable Text") ? "Segoe UI Variable Text" : "Segoe UI", size, style);

    /// <summary>The Windows icon font: Segoe Fluent Icons on Windows 11, Segoe MDL2 Assets on 10. Same code points.</summary>
    public static Font IconFont(float size) =>
        new(FontFamily.Families.Any(f => f.Name == "Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets", size);

    static Color Hex(int rgb) => Color.FromArgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
}

/// <summary>Glyphs of the Windows icon font.</summary>
public static class Glyph
{
    public const string Play = "";
    public const string Pause = "";
    public const string Stop = "";
    public const string Clear = "";
    public const string Keyboard = "";
}
