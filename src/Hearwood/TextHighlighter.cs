using System.Runtime.InteropServices;

namespace Hearwood;

/// <summary>
/// Lights one span of a RichTextBox through the control's Text Object Model: a range's background is set
/// without moving the user's selection, and with undo suspended, so highlights never become undo steps.
/// Offsets are UTF-16 positions in <see cref="RichTextBox.Text"/> (a line break is one character there and in TOM).
/// The box scrolls to keep the lit word in view while the reader follows it: if they scroll away from it,
/// it is left where they put it until they scroll back.
/// </summary>
sealed class TextHighlighter(RichTextBox box, Color highlight)
{
    const int WM_USER = 0x400;
    const int EM_GETOLEINTERFACE = WM_USER + 60;
    const int EM_GETFIRSTVISIBLELINE = 0xCE;
    const int EM_LINESCROLL = 0xB6;
    const int TomSuspend = -9999995;
    const int TomResume = -9999994;
    const int TomAutoColor = -9999997;

    dynamic? _doc;
    (int Start, int Length)? _shown;

    /// <summary>True while a highlight is being applied, so the box's TextChanged can ignore it.</summary>
    public bool IsPainting { get; private set; }

    [DllImport("user32.dll")]
    static extern nint SendMessage(nint hWnd, int msg, nint wParam, out nint lParam);

    [DllImport("user32.dll")]
    static extern nint SendMessage(nint hWnd, int msg, nint wParam, nint lParam);

    public void Show(int start, int length)
    {
        if (!box.IsHandleCreated)
            return;
        bool following = _shown is not { } old || IsInView(old.Start);
        Paint(() =>
        {
            if (_shown is { } old)
                Doc().Range(old.Start, old.Start + old.Length).Font.BackColor = TomAutoColor;
            Doc().Range(start, start + length).Font.BackColor = ColorTranslator.ToWin32(highlight);
            _shown = (start, length);
        });
        if (following && !IsFullyInView(start))
            ScrollToLine(box.GetLineFromCharIndex(start) - 1);
    }

    /// <summary>True when any of the line holding <paramref name="index"/> shows.</summary>
    bool IsInView(int index)
    {
        int top = box.GetPositionFromCharIndex(index).Y;
        return top < box.ClientSize.Height && top + box.Font.Height > 0;
    }

    bool IsFullyInView(int index)
    {
        int top = box.GetPositionFromCharIndex(index).Y;
        return top >= 0 && top + box.Font.Height <= box.ClientSize.Height;
    }

    /// <summary>Scrolls so <paramref name="line"/> (a display line, counting wrapped ones) is the first in view, as far as the text allows.</summary>
    void ScrollToLine(int line)
    {
        int first = (int)SendMessage(box.Handle, EM_GETFIRSTVISIBLELINE, 0, 0);
        SendMessage(box.Handle, EM_LINESCROLL, 0, Math.Max(0, line) - first);
    }

    /// <summary>
    /// Removes the highlight from the whole text: after an edit the lit word may have moved, and text typed
    /// beside it takes on its background.
    /// </summary>
    public void Clear()
    {
        if (_shown is null)
            return;
        _shown = null;
        Paint(() => Doc().Range(0, box.TextLength + 1).Font.BackColor = TomAutoColor);
    }

    void Paint(Action apply)
    {
        if (!box.IsHandleCreated)
            return;
        IsPainting = true;
        try
        {
            Doc().Undo(TomSuspend);
            try { apply(); }
            finally { Doc().Undo(TomResume); }
        }
        catch (COMException) { _shown = null; }
        finally
        {
            IsPainting = false;
        }
    }

    dynamic Doc()
    {
        if (_doc is null)
        {
            SendMessage(box.Handle, EM_GETOLEINTERFACE, 0, out var unknown);
            if (unknown == 0)
                throw new COMException("The text box has no Text Object Model.");
            try { _doc = Marshal.GetObjectForIUnknown(unknown); }
            finally { Marshal.Release(unknown); }
        }
        return _doc!;
    }
}
