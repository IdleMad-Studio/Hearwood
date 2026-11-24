using System.ComponentModel;

namespace Hearwood;

/// <summary>Registers global hotkeys on a hidden message-only window.</summary>
public sealed class HotkeyManager : NativeWindow, IDisposable
{
    /// <summary>Outside the ids <see cref="Register"/> hands out (they must stay below 0xC000).</summary>
    const int ProbeId = 0xBFFF;

    readonly Dictionary<int, Action> _actions = new();
    int _nextId = 1;

    public HotkeyManager() => CreateHandle(new CreateParams { Parent = new IntPtr(-3) }); // HWND_MESSAGE

    /// <returns>false if another program already owns the combination.</returns>
    public bool Register(Hotkey hotkey, Action action)
    {
        int id = _nextId++;
        if (!Native.RegisterHotKey(Handle, id, hotkey.NativeModifiers | Native.MOD_NOREPEAT, (uint)hotkey.Key)) return false;
        _actions[id] = action;
        return true;
    }

    /// <summary>Whether <paramref name="hotkey"/> could be registered now. Hotkeys this manager holds count as taken.</summary>
    public bool IsFree(Hotkey hotkey)
    {
        if (!Native.RegisterHotKey(Handle, ProbeId, hotkey.NativeModifiers, (uint)hotkey.Key)) return false;
        Native.UnregisterHotKey(Handle, ProbeId);
        return true;
    }

    public void UnregisterAll()
    {
        foreach (var id in _actions.Keys) Native.UnregisterHotKey(Handle, id);
        _actions.Clear();
        _nextId = 1;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Native.WM_HOTKEY && _actions.TryGetValue((int)m.WParam, out var action))
            action();
        else
            base.WndProc(ref m);
    }

    public void Dispose()
    {
        UnregisterAll();
        if (Handle != IntPtr.Zero) DestroyHandle();
    }
}
