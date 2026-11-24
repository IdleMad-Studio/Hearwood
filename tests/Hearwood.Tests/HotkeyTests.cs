using System.Text.Json;
using System.Windows.Forms;

namespace Hearwood.Tests;

public class HotkeyTests
{
    [Theory]
    [InlineData("Ctrl+Alt+S")]
    [InlineData("Alt+Shift+F5")]
    [InlineData("Ctrl+7")]
    [InlineData("Ctrl+Alt+PageDown")]
    [InlineData("Ctrl+Shift+Enter")]
    [InlineData("Ctrl+Alt+Shift+OemPeriod")]
    public void Text_round_trips(string text)
    {
        Assert.True(Hotkey.TryParse(text, out var hotkey));
        Assert.Equal(text, hotkey.ToString());
    }

    [Fact]
    public void Parsing_ignores_case_spaces_and_modifier_order()
    {
        Assert.True(Hotkey.TryParse(" alt + control + s ", out var hotkey));
        Assert.Equal(new Hotkey(Keys.Control | Keys.Alt, Keys.S), hotkey);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("S")]              // no modifier
    [InlineData("Shift+S")]        // Shift alone would take a typed capital
    [InlineData("Ctrl+Alt")]       // no key
    [InlineData("Ctrl+ControlKey")] // a modifier as the key
    [InlineData("Ctrl+Alt+65")]    // a number Enum.TryParse would accept
    [InlineData("Ctrl+A, B")]      // a list Enum.TryParse would accept
    [InlineData("Win+S")]
    [InlineData("Ctrl+Nonsense")]
    public void Unusable_text_is_rejected(string? text) => Assert.False(Hotkey.TryParse(text, out _));

    [Fact]
    public void Native_modifiers_match_register_hotkey() =>
        Assert.Equal(Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_SHIFT,
            new Hotkey(Keys.Control | Keys.Alt | Keys.Shift, Keys.S).NativeModifiers);

    [Fact]
    public void The_defaults_are_the_documented_hotkeys()
    {
        var cfg = new Config();
        Assert.Equal("Ctrl+Alt+S", cfg.GetHotkey(HotkeyAction.Read).ToString());
        Assert.Equal("Ctrl+Alt+X", cfg.GetHotkey(HotkeyAction.Stop).ToString());
        Assert.Equal("Ctrl+Alt+Up", cfg.GetHotkey(HotkeyAction.Faster).ToString());
        Assert.Equal("Ctrl+Alt+Down", cfg.GetHotkey(HotkeyAction.Slower).ToString());
    }

    [Fact]
    public void Every_default_is_valid_and_distinct()
    {
        Assert.All(HotkeyActions.All, a => Assert.True(a.Default().IsValid));
        Assert.Equal(HotkeyActions.All.Count, HotkeyActions.All.Select(a => a.Default()).Distinct().Count());
    }

    [Fact]
    public void Hotkeys_are_saved_as_text_by_action()
    {
        var cfg = new Config();
        cfg.SetHotkey(HotkeyAction.Stop, new Hotkey(Keys.Alt | Keys.Shift, Keys.R));
        var json = JsonSerializer.Serialize(cfg, Config.JsonOptions);
        Assert.Contains("\"Stop\": \"Alt+Shift+R\"", json);
        Assert.Contains("\"Read\": \"Ctrl+Alt+S\"", json);

        var back = JsonSerializer.Deserialize<Config>(json)!;
        Assert.Equal(new Hotkey(Keys.Alt | Keys.Shift, Keys.R), back.GetHotkey(HotkeyAction.Stop));
        Assert.Equal(HotkeyAction.Read.Default(), back.GetHotkey(HotkeyAction.Read));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"Hotkeys\":null}")]
    [InlineData("{\"Hotkeys\":{}}")]
    [InlineData("{\"Hotkeys\":{\"Read\":\"Shift+S\",\"Stop\":null}}")]
    public void A_missing_or_unusable_stored_hotkey_gives_the_default(string json)
    {
        var cfg = JsonSerializer.Deserialize<Config>(json)!;
        Assert.All(HotkeyActions.All, a => Assert.Equal(a.Default(), cfg.GetHotkey(a)));
    }

    [Fact]
    public void Setting_a_hotkey_after_a_null_list_keeps_the_others_at_their_defaults()
    {
        var cfg = JsonSerializer.Deserialize<Config>("{\"Hotkeys\":null}")!;
        cfg.SetHotkey(HotkeyAction.Faster, new Hotkey(Keys.Control, Keys.Oemplus));
        Assert.Equal(new Hotkey(Keys.Control, Keys.Oemplus), cfg.GetHotkey(HotkeyAction.Faster));
        Assert.Equal(HotkeyAction.Slower.Default(), cfg.GetHotkey(HotkeyAction.Slower));
    }
}
