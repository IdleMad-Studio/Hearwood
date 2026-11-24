namespace Hearwood;

/// <summary>
/// Copies the current selection in the foreground app via Ctrl+C, then restores the
/// previous clipboard text. Must be called on the UI (STA) thread.
/// </summary>
public static class SelectionGrabber
{
    public static async Task<string?> GrabAsync()
    {
        // The hotkey's own modifiers (Ctrl+Alt) are still down; wait so our Ctrl+C isn't Ctrl+Alt+C.
        for (int i = 0; i < 30 && Native.AnyModifierDown(); i++) await Task.Delay(50);

        string? previous = TryGetText();
        uint before = Native.GetClipboardSequenceNumber();

        Native.SendCtrlC();
        for (int i = 0; i < 15 && Native.GetClipboardSequenceNumber() == before; i++) await Task.Delay(40);

        string? selected = null;
        if (Native.GetClipboardSequenceNumber() != before)
        {
            selected = TryGetText();
            if (previous != null) TrySetText(previous);
        }

        // Nothing selected: read whatever is on the clipboard.
        return !string.IsNullOrWhiteSpace(selected) ? selected : previous;
    }

    static string? TryGetText()
    {
        for (int i = 0; i < 5; i++)
        {
            try { return Clipboard.ContainsText() ? Clipboard.GetText() : null; }
            catch (System.Runtime.InteropServices.ExternalException) { Thread.Sleep(30); }
        }
        return null;
    }

    static void TrySetText(string text)
    {
        for (int i = 0; i < 5; i++)
        {
            try { Clipboard.SetText(text); return; }
            catch (System.Runtime.InteropServices.ExternalException) { Thread.Sleep(30); }
        }
    }
}
