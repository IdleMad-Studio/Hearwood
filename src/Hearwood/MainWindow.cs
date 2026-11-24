using Hearwood.Pocket;
using Hearwood.Text;
using Hearwood.Ui;

namespace Hearwood;

/// <summary>
/// The text box and its controls, like the window of another TTS app by the same author: a header with the voice and speed, the text in a
/// card, and the actions below it. Closing it only hides it: the tray and the hotkeys keep working. Words
/// spoken from the box are highlighted until the box is edited.
/// </summary>
public sealed class MainWindow : Form
{
    readonly Config _cfg;
    readonly PocketService _pocket;
    readonly SpeechPlayer _player;
    readonly Theme _theme = Theme.Current;

    readonly RichTextBox _text = new()
    {
        Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, AcceptsTab = false, DetectUrls = false,
        ScrollBars = RichTextBoxScrollBars.Vertical, HideSelection = false,
    };
    /// <summary>Speak when idle, Pause while speaking, Resume while paused; as wide as its widest label, so the row does not shift.</summary>
    readonly PillButton _speak;
    readonly PillButton _stop;
    readonly PillButton _clear;
    readonly PillButton _smaller;
    readonly PillButton _larger;
    readonly ToolTip _tips = new();
    readonly PillButton _hotkeysButton;
    readonly PillButton _substitutionsButton;
    readonly ContextMenuStrip _hotkeysMenu = new();
    readonly ContextMenuStrip _textMenu = new();
    readonly ComboBox _voice = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 190, FlatStyle = FlatStyle.Flat };
    readonly ComboBox _speed = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 84, FlatStyle = FlatStyle.Flat };
    readonly Label _status = new() { AutoSize = true, Anchor = AnchorStyles.Left };
    readonly Label _hint = new() { AutoSize = true };
    readonly System.Windows.Forms.Timer _highlightTimer = new() { Interval = 40 };

    readonly TextHighlighter _highlighter;
    /// <summary>The text box's current contents as an utterance tag; replaced on every edit, so old words are never lit.</summary>
    object _boxVersion = new();
    WordSpan? _lit;
    bool _updatingControls;
    /// <summary>The configured size the box's font was last set from; its font may since have been scaled for a monitor.</summary>
    float _textSizeShown;
    /// <summary>Ctrl+wheel movement short of a whole notch, as a precision touchpad sends it.</summary>
    int _wheelRest;
    /// <summary>Where the box was last right-clicked; null when its menu is opened from the keyboard, which reads from the caret.</summary>
    Point? _rightClick;
    /// <summary>Where "Read from here" starts, set as the box's menu opens.</summary>
    int _readFrom;

    public MainWindow(Config cfg, PocketService pocket, SpeechPlayer player)
    {
        _cfg = cfg;
        _pocket = pocket;
        _player = player;
        // Every size below is at 100% scale; WinForms scales the window and its controls to the monitor's DPI,
        // when it opens and when it moves to a monitor with another scale. Layout stays suspended until the
        // controls are in place, because the first scaling happens when it resumes.
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96f, 96f);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "Hearwood";
        Icon = AppIcon.Window();
        BackColor = _theme.Window;
        ForeColor = _theme.Text;
        Font = Theme.UiFont(10f);
        ClientSize = new Size(820, 520);
        MinimumSize = new Size(700, 380);
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = true;

        _speak = new PillButton(_theme, primary: true) { Glyph = Glyph.Play, Text = "Speak", WidthFor = ["Pause", "Resume"] };
        _stop = Button(Glyph.Stop, "Stop");
        _stop.Enabled = false;
        _clear = Button(Glyph.Clear, "Clear");
        // Letters rather than the icon font's font-size glyphs, whose accent marks are too small to read.
        _smaller = Button("", "A−");
        _smaller.AccessibleName = "Smaller text";
        _larger = Button("", "A+");
        _larger.AccessibleName = "Larger text";
        _larger.Margin = new Padding(0, 0, 16, 0);
        _tips.SetToolTip(_smaller, "Smaller text (Ctrl+Minus)");
        _tips.SetToolTip(_larger, "Larger text (Ctrl+Plus)");
        _hotkeysButton = new PillButton(_theme) { Glyph = Glyph.Keyboard, Text = HotkeyDialog.ChangeLabel, Font = Theme.UiFont(9f), BaseHeight = 30, Margin = Padding.Empty };
        _substitutionsButton = Button("", TrayApp.SubstitutionsLabel);
        _substitutionsButton.Margin = new Padding(0, 0, 16, 0);

        foreach (var combo in new[] { _voice, _speed })
        {
            combo.BackColor = _theme.Surface;
            combo.ForeColor = _theme.Text;
            combo.Font = Theme.UiFont(10f);
            combo.Margin = Padding.Empty;
            combo.Anchor = AnchorStyles.Left;
        }
        // Space before the "Speed" caption.
        _voice.Margin = new Padding(0, 0, 18, 0);
        // The box shows the voice's name; the list shows its label too, which is wider than the box.
        _voice.DrawMode = DrawMode.OwnerDrawFixed;
        _voice.ItemHeight = _speed.ItemHeight;
        // Measured as the list opens, so it fits the labels at the window's DPI now.
        _voice.DropDown += (_, _) => _voice.DropDownWidth = Math.Max(_voice.Width, _voice.Items.Cast<string>()
            .Select(v => TextRenderer.MeasureText(VoiceLibrary.Label(v), _voice.Font).Width + SystemInformation.GetVerticalScrollBarWidthForDpi(DeviceDpi) + LogicalToDeviceUnits(8))
            .DefaultIfEmpty(0).Max());
        // An owner-drawn list keeps a fixed item height. Follow the speed box, whose height tracks its font, once it
        // has resized for a new DPI (on DpiChanged it still has the old height).
        _speed.SizeChanged += (_, _) => _voice.ItemHeight = _speed.ItemHeight;
        _voice.DrawItem += (_, e) =>
        {
            if (e.Index < 0) return;
            var name = _voice.Items[e.Index] as string ?? "";
            e.DrawBackground();
            TextRenderer.DrawText(e.Graphics, e.State.HasFlag(DrawItemState.ComboBoxEdit) ? name : VoiceLibrary.Label(name), _voice.Font,
                e.Bounds, e.ForeColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        };
        _text.BackColor = _theme.Surface;
        _text.ForeColor = _theme.Text;
        _textSizeShown = _cfg.TextSize;
        _text.Font = Theme.UiFont(_textSizeShown);
        _status.ForeColor = _theme.Muted;
        _status.Font = Theme.UiFont(9f);

        _highlighter = new TextHighlighter(_text, _theme.Highlight);

        // Header: the app's mark on the left, the voice and speed on the right.
        var brand = Row();
        brand.Controls.Add(new PictureBox
        {
            // Drawn from the 64 px icon and shrunk to fit, so it stays sharp up to 200% as the box scales.
            Image = AppIcon.Bitmap(64), SizeMode = PictureBoxSizeMode.Zoom,
            Size = new Size(28, 36), Margin = new Padding(0, 0, 10, 0), Anchor = AnchorStyles.Left,
        });
        brand.Controls.Add(new Label { Text = "Hearwood", AutoSize = true, Font = Theme.UiFont(15f, FontStyle.Bold), ForeColor = _theme.Text, Anchor = AnchorStyles.Left });
        var choices = Row();
        choices.Controls.AddRange([Caption("Voice"), _voice, Caption("Speed"), _speed]);
        var header = Bar(brand, choices);

        var card = new Card(_theme) { Dock = DockStyle.Fill, Margin = new Padding(0, 12, 0, 14) };
        card.Controls.Add(_text);

        var actions = Bar(Row(_speak, _stop), Row(_substitutionsButton, _smaller, _larger, _clear));
        _clear.Margin = Padding.Empty;

        _hint.ForeColor = _theme.Muted;
        _hint.Font = Theme.UiFont(9f);
        _hint.Anchor = AnchorStyles.Left;
        _hint.Margin = new Padding(0, 0, 10, 0);
        ShowReadHotkey(_cfg.GetHotkey(HotkeyAction.Read));
        var footer = Bar(_status, Row(_hint, _hotkeysButton));
        footer.Margin = new Padding(2, 10, 0, 0);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(22, 16, 22, 12), BackColor = _theme.Window,
        };
        // One column, the window's width: left to size itself, it can measure wider than the window at its minimum size.
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(header, 0, 0);
        root.Controls.Add(card, 0, 1);
        root.Controls.Add(actions, 0, 2);
        root.Controls.Add(footer, 0, 3);
        Controls.Add(root);
        ResumeLayout(false);
        PerformLayout();

        foreach (var s in Config.SpeedChoices)
            _speed.Items.Add(new SpeedItem(s));
        RefreshVoices();
        RefreshSpeed();
        RefreshTextSize();

        _speak.Click += (_, _) =>
        {
            if (_player.IsSpeaking) _player.TogglePause();
            else SpeakBox();
        };
        _stop.Click += (_, _) => _player.Stop();
        _clear.Click += (_, _) => { ReplaceText(""); _text.Focus(); };
        _substitutionsButton.Click += (_, _) => SubstitutionsChosen?.Invoke(null);
        _smaller.Click += (_, _) => StepTextSize(-1);
        _larger.Click += (_, _) => StepTextSize(+1);
        _hotkeysButton.Click += (_, _) =>
        {
            // Built on each click so the items show the hotkeys as they are now.
            _hotkeysMenu.Items.Clear();
            _hotkeysMenu.Items.AddRange(HotkeyDialog.MenuItems(_cfg, a => HotkeyChangeChosen?.Invoke(a)));
            _hotkeysMenu.Show(_hotkeysButton, new Point(0, _hotkeysButton.Height));
        };
        _voice.SelectedIndexChanged += (_, _) =>
        {
            if (!_updatingControls && _voice.SelectedItem is string v) VoiceChosen?.Invoke(v);
        };
        _speed.SelectedIndexChanged += (_, _) =>
        {
            if (!_updatingControls && _speed.SelectedItem is SpeedItem s) RateChosen?.Invoke(s.Value);
        };

        _text.TextChanged += (_, _) =>
        {
            if (_highlighter.IsPainting)
                return;
            // Any edit ends highlighting for what was spoken before it.
            _boxVersion = new object();
            ClearHighlight();
        };
        _text.KeyDown += OnTextKeyDown;
        _text.MouseWheel += OnTextMouseWheel;

        BuildTextMenu();
        _text.ContextMenuStrip = _textMenu;
        _text.MouseDown += (_, e) => { if (e.Button == MouseButtons.Right) _rightClick = e.Location; };

        _player.StateChanged += () => BeginInvokeIfAlive(UpdatePlaybackControls);
        _highlightTimer.Tick += (_, _) => UpdateHighlight();
        _highlightTimer.Start();

        FormClosing += (_, e) =>
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
            }
        };
    }

    PillButton Button(string glyph, string text) => new(_theme) { Glyph = glyph, Text = text };

    Label Caption(string text) => new()
    {
        Text = text, AutoSize = true, ForeColor = _theme.Muted, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 8, 0),
    };

    /// <summary>Controls side by side on one line.</summary>
    FlowLayoutPanel Row(params Control[] controls)
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty, BackColor = _theme.Window };
        row.Controls.AddRange(controls);
        return row;
    }

    /// <summary>A full-width line with <paramref name="left"/> at the start and <paramref name="right"/> at the end.</summary>
    TableLayoutPanel Bar(Control left, Control right)
    {
        var bar = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty, BackColor = _theme.Window };
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        left.Anchor = AnchorStyles.Left;
        right.Anchor = AnchorStyles.Right;
        bar.Controls.Add(left, 0, 0);
        bar.Controls.Add(right, 2, 0);
        return bar;
    }

    /// <summary>The user picked a voice in the window.</summary>
    public event Action<string>? VoiceChosen;

    /// <summary>The user picked a speed in the window.</summary>
    public event Action<double>? RateChosen;

    /// <summary>The user picked a text size in the window, in points.</summary>
    public event Action<float>? TextSizeChosen;

    /// <summary>The user asked for the word substitutions, with the word to start an entry for, if any.</summary>
    public event Action<string?>? SubstitutionsChosen;

    /// <summary>The user asked to change an action's hotkey from the window.</summary>
    public event Action<HotkeyAction>? HotkeyChangeChosen;

    public string Voice => _cfg.Voice is { } v && _pocket.Voices.Exists(v) ? v : PocketService.DefaultVoice;

    public void ShowAndActivate()
    {
        if (!Visible) Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
    }

    /// <summary>Puts <paramref name="text"/> in the box as one undo step and speaks it, with highlighting.</summary>
    public void SpeakInBox(string text)
    {
        ReplaceText(text);
        SpeakBox();
    }

    public void SetStatus(string text) => _status.Text = text;

    public void ShowReadHotkey(Hotkey hotkey) => _hint.Text = $"{hotkey} reads selected text in any app";

    public void RefreshVoices()
    {
        _updatingControls = true;
        _voice.Items.Clear();
        foreach (var v in _pocket.Voices.List())
            _voice.Items.Add(v);
        _voice.SelectedItem = Voice;
        _updatingControls = false;
    }

    public void RefreshSpeed()
    {
        _updatingControls = true;
        _speed.SelectedItem = _speed.Items.Cast<SpeedItem>().FirstOrDefault(i => Math.Abs(i.Value - _cfg.Rate) < 1e-9);
        _updatingControls = false;
    }

    /// <summary>Sets the box's font to the configured size, at the scale WinForms has given it for the monitor.</summary>
    public void RefreshTextSize()
    {
        _smaller.Enabled = _cfg.TextSize > Config.TextSizeChoices[0];
        _larger.Enabled = _cfg.TextSize < Config.TextSizeChoices[^1];
        if (_cfg.TextSize == _textSizeShown)
            return;
        float monitorScale = _text.Font.Size / _textSizeShown;
        _textSizeShown = _cfg.TextSize;
        _highlighter.SetFont(Theme.UiFont(_textSizeShown * monitorScale));
    }

    void StepTextSize(int steps) => TextSizeChosen?.Invoke(Config.StepTextSize(_cfg.TextSize, steps));

    /// <summary>Speaks the <paramref name="part"/> of the box's text (all of it by default), lighting each word as it is heard.</summary>
    void SpeakBox(Range? part = null)
    {
        var text = _text.Text;
        var range = part ?? Range.All;
        if (string.IsNullOrWhiteSpace(text[range]))
        {
            SetStatus("Nothing to read.");
            return;
        }
        ClearHighlight();
        _player.Speak(text, Voice, _boxVersion, range);
    }

    /// <summary>
    /// The box's right-click menu: reading, then the usual editing commands (RichTextBox has none of its own). The
    /// shortcuts are only shown: the box handles the keys itself, so Ctrl+V still pastes plain text.
    /// </summary>
    void BuildTextMenu()
    {
        ToolStripMenuItem Item(string text, string? keys, Action click)
        {
            var item = new ToolStripMenuItem(text) { ShortcutKeyDisplayString = keys };
            item.Click += (_, _) => click();
            return item;
        }
        var readFromHere = Item("Read from here", null, () => SpeakBox(_readFrom..));
        var readSelection = Item("Read selection", null, () =>
        {
            int start = _text.SelectionStart, end = start + _text.SelectionLength;
            // The selection's colour would hide the lit word.
            _text.Select(start, 0);
            SpeakBox(start..end);
        });
        var pasteAndRead = Item("Paste and read", null, () => { if (ClipboardText() is { } t) SpeakInBox(t); });
        var undo = Item("Undo", "Ctrl+Z", _text.Undo);
        var cut = Item("Cut", "Ctrl+X", _text.Cut);
        var copy = Item("Copy", "Ctrl+C", _text.Copy);
        var paste = Item("Paste", "Ctrl+V", PastePlain);
        var delete = Item("Delete", "Del", () => _text.SelectedText = "");
        var selectAll = Item("Select all", "Ctrl+A", _text.SelectAll);
        string? word = null;
        var substitute = Item("Add substitution…", null, () => SubstitutionsChosen?.Invoke(word));
        _textMenu.Items.AddRange([
            readFromHere, readSelection, pasteAndRead, new ToolStripSeparator(),
            substitute, new ToolStripSeparator(),
            undo, new ToolStripSeparator(), cut, copy, paste, delete, new ToolStripSeparator(), selectAll,
        ]);

        _textMenu.Opening += (_, _) =>
        {
            var text = _text.Text;
            _readFrom = SpeechPlan.WordStartAt(text, _rightClick is { } p ? CharIndexAt(p) : _text.SelectionStart);
            _rightClick = null;
            bool selected = _text.SelectionLength > 0;
            bool clipboardText = !string.IsNullOrWhiteSpace(ClipboardText());
            readFromHere.Enabled = !string.IsNullOrWhiteSpace(text[_readFrom..]);
            readSelection.Available = selected && !string.IsNullOrWhiteSpace(_text.SelectedText);
            pasteAndRead.Enabled = clipboardText;
            undo.Enabled = _text.CanUndo;
            cut.Enabled = copy.Enabled = delete.Enabled = selected;
            paste.Enabled = clipboardText;
            selectAll.Enabled = _text.TextLength > 0;
            // The selection when it is a word or short phrase on one line; otherwise the word clicked.
            var chosen = _text.SelectedText.Trim();
            word = chosen.Length is > 0 and <= 60 && !chosen.Contains('\n') ? chosen : Substitutions.WordAt(text, _readFrom);
            if (word.Length == 0) word = null;
            substitute.Text = word is null ? "Add substitution…" : $"Add substitution for “{Shorten(word)}”…";
        };
    }

    static string Shorten(string s) => s.Length <= 30 ? s : s[..29] + "…";

    /// <summary>Pastes the clipboard as plain text: formatting from a web page or document has no place in what is read aloud.</summary>
    void PastePlain()
    {
        if (ClipboardText() is { } text)
            _text.SelectedText = text;
    }

    /// <summary>The clipboard's text; null when it has none or another program has it open.</summary>
    static string? ClipboardText()
    {
        try { return Clipboard.ContainsText() ? Clipboard.GetText() : null; }
        catch (System.Runtime.InteropServices.ExternalException) { return null; }
    }

    /// <summary>The character at <paramref name="point"/> in the box; the end of the text for a point below its last line.</summary>
    int CharIndexAt(Point point)
    {
        if (_text.TextLength == 0)
            return 0;
        // RichEdit gives the nearest character, which for a click in the empty space below the text is the last one.
        int lastLineTop = _text.GetPositionFromCharIndex(_text.TextLength - 1).Y;
        return point.Y >= lastLineTop + _text.Font.Height ? _text.TextLength : _text.GetCharIndexFromPosition(point);
    }

    /// <summary>Replaces the whole text as a single edit, so one Ctrl+Z brings the old text back.</summary>
    void ReplaceText(string text)
    {
        _text.SelectAll();
        _text.SelectedText = text;
        _text.Select(0, 0);
    }

    void OnTextKeyDown(object? sender, KeyEventArgs e)
    {
        if ((e.Control && e.KeyCode == Keys.V) || (e.Shift && e.KeyCode == Keys.Insert))
        {
            e.SuppressKeyPress = true;
            PastePlain();
            return;
        }
        // Text size, as in a browser. Handled here, these keys also never reach RichEdit's own Ctrl+= (subscript).
        if (e.Control && !e.Alt)
        {
            switch (e.KeyCode)
            {
                case Keys.Oemplus or Keys.Add: StepTextSize(+1); break;
                case Keys.OemMinus or Keys.Subtract: StepTextSize(-1); break;
                case Keys.D0 or Keys.NumPad0: TextSizeChosen?.Invoke(Config.DefaultTextSize); break;
                default: return;
            }
            e.SuppressKeyPress = true;
        }
    }

    /// <summary>Ctrl+wheel steps the text size; left to RichEdit, it would zoom instead, unsaved and undone by Clear.</summary>
    void OnTextMouseWheel(object? sender, MouseEventArgs e)
    {
        if ((ModifierKeys & Keys.Control) == 0)
            return;
        if (e is HandledMouseEventArgs handled)
            handled.Handled = true;
        _wheelRest += e.Delta;
        int steps = _wheelRest / SystemInformation.MouseWheelScrollDelta;
        _wheelRest %= SystemInformation.MouseWheelScrollDelta;
        if (steps != 0)
            StepTextSize(steps);
    }

    void UpdatePlaybackControls()
    {
        bool speaking = _player.IsSpeaking;
        _stop.Enabled = speaking;
        (_speak.Glyph, _speak.Text) = !speaking ? (Glyph.Play, "Speak") : _player.IsPaused ? (Glyph.Play, "Resume") : (Glyph.Pause, "Pause");
        if (!speaking)
            ClearHighlight();
    }

    void UpdateHighlight()
    {
        if (!ReferenceEquals(_player.CurrentTag, _boxVersion))
        {
            if (_lit is not null) ClearHighlight();
            return;
        }
        var word = _player.CurrentWord();
        if (word == _lit)
            return;
        if (word is { } w && w.Start + w.Length <= _text.TextLength)
        {
            _highlighter.Show(w.Start, w.Length);
            _lit = w;
        }
    }

    void ClearHighlight()
    {
        _highlighter.Clear();
        _lit = null;
    }

    void BeginInvokeIfAlive(Action action)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try { BeginInvoke(action); } catch (InvalidOperationException) { }
    }

    sealed record SpeedItem(double Value)
    {
        public override string ToString() => $"{Value:0.##}×";
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _highlightTimer.Dispose(); _hotkeysMenu.Dispose(); _textMenu.Dispose(); _tips.Dispose(); }
        base.Dispose(disposing);
    }
}
