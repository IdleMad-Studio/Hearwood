using Hearwood.Ui;

namespace Hearwood;

/// <summary>
/// Asks for a new hotkey for one action. Shows each combination as it is pressed and says why one can't be used. The app's
/// hotkeys should be off while it is open, or pressing one would act instead of showing here.
/// </summary>
public sealed class HotkeyDialog : Form
{
    const int WM_SYSCOMMAND = 0x0112, SC_KEYMENU = 0xF100;

    readonly IReadOnlyDictionary<Hotkey, HotkeyAction> _taken;
    readonly Func<Hotkey, bool> _isFree;
    readonly Label _keys = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter };
    readonly Label _problem = new() { AutoSize = true, MaximumSize = new Size(380, 0), Margin = new Padding(0, 8, 0, 0) };
    readonly PillButton _save;
    Hotkey? _candidate;

    /// <param name="action">The action whose hotkey is being changed.</param>
    /// <param name="current">Its hotkey now, shown until another is pressed.</param>
    /// <param name="taken">Hearwood's other hotkeys, and what each does.</param>
    /// <param name="isFree">Whether no other program holds a combination.</param>
    public HotkeyDialog(HotkeyAction action, Hotkey current, IReadOnlyDictionary<Hotkey, HotkeyAction> taken, Func<Hotkey, bool> isFree)
    {
        _taken = taken;
        _isFree = isFree;
        var theme = Theme.Current;
        // Sizes below are at 100% scale; WinForms scales them to the monitor's DPI when layout resumes (see MainWindow).
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96f, 96f);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = action.Name() + " hotkey";
        Icon = AppIcon.Window();
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        BackColor = theme.Window;
        ForeColor = theme.Text;
        Font = Theme.UiFont(10f);

        var prompt = new Label
        {
            Text = $"Press the keys that should {action.Does()}: Ctrl or Alt (Shift too, if you like) with one other key.",
            AutoSize = true, MaximumSize = new Size(380, 0), Margin = new Padding(0, 0, 0, 12),
        };
        _keys.Font = Theme.UiFont(16f, FontStyle.Bold);
        _keys.Text = current.ToString();
        var card = new Card(theme) { Size = new Size(380, 72), Margin = Padding.Empty };
        card.Controls.Add(_keys);
        _problem.ForeColor = theme.Muted;
        _problem.Font = Theme.UiFont(9f);

        var reset = new PillButton(theme) { Text = "Use " + action.Default() };
        reset.Click += (_, _) => Consider(action.Default());
        _save = new PillButton(theme, primary: true) { Text = "Save", DialogResult = DialogResult.OK, Enabled = false };
        _save.Click += (_, _) => Chosen = _candidate;
        var cancel = new PillButton(theme) { Text = "Cancel", DialogResult = DialogResult.Cancel, Margin = Padding.Empty };
        CancelButton = cancel;

        var buttons = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 4, Margin = new Padding(0, 16, 0, 0) };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        buttons.Controls.Add(reset, 0, 0);
        buttons.Controls.Add(_save, 2, 0);
        buttons.Controls.Add(cancel, 3, 0);

        var root = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Padding = new Padding(22, 18, 22, 16) };
        root.Controls.Add(prompt);
        root.Controls.Add(card);
        root.Controls.Add(_problem);
        root.Controls.Add(buttons);
        Controls.Add(root);
        ResumeLayout(false);
        PerformLayout();
    }

    /// <summary>The label of the tray submenu and the window's button that offer <see cref="MenuItems"/>.</summary>
    public const string ChangeLabel = "Change Hotkeys";

    /// <summary>One item per action, showing its hotkey now ("Stop: Ctrl+Alt+X…"); clicking one calls <paramref name="change"/>.</summary>
    public static ToolStripItem[] MenuItems(Config cfg, Action<HotkeyAction> change) =>
        HotkeyActions.All.Select(action =>
        {
            var item = new ToolStripMenuItem($"{action.Name()}: {cfg.GetHotkey(action)}…");
            item.Click += (_, _) => change(action);
            return (ToolStripItem)item;
        }).ToArray();

    /// <summary>The new hotkey, set when the user saves; null if they didn't.</summary>
    public Hotkey? Chosen { get; private set; }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        var modifiers = keyData & Keys.Modifiers;
        var key = keyData & Keys.KeyCode;

        // Keys that work the dialog: Enter saves, Esc cancels, Tab/Shift+Tab/Space move and press, Alt+F4 closes.
        if (modifiers == Keys.None && key == Keys.Enter)
        {
            if (_save.Enabled) _save.PerformClick();
            return true;
        }
        if ((modifiers == Keys.None && key is Keys.Escape or Keys.Tab or Keys.Space)
            || (modifiers == Keys.Shift && key == Keys.Tab)
            || (modifiers == Keys.Alt && key == Keys.F4))
            return base.ProcessCmdKey(ref msg, keyData);

        if (Hotkey.IsModifierKey(key))
        {
            _keys.Text = Hotkey.ModifierText(modifiers) + "…";
            // A combination another program has registered goes to that program, so its key never arrives here.
            _problem.Text = "Now add a key. If the key never shows here, another program already uses that combination.";
            _candidate = null;
            _save.Enabled = false;
        }
        else
            Consider(new Hotkey(modifiers, key));
        return true;
    }

    void Consider(Hotkey hotkey)
    {
        _keys.Text = hotkey.ToString();
        string? problem =
            (hotkey.Modifiers & (Keys.Control | Keys.Alt)) == 0 ? "Add Ctrl or Alt, so the key still types normally." :
            !hotkey.IsValid ? "That key can't be used for a hotkey." :
            _taken.TryGetValue(hotkey, out var other) ? $"{hotkey} is already the {other.Name()} hotkey." :
            !_isFree(hotkey) ? $"Another program already uses {hotkey}." :
            null;
        _problem.Text = problem ?? "";
        _candidate = problem is null ? hotkey : null;
        _save.Enabled = _candidate is not null;
    }

    protected override void WndProc(ref Message m)
    {
        // Alt pressed and released on its own would open the window menu, which then takes the next key press.
        if (m.Msg == WM_SYSCOMMAND && ((int)m.WParam & 0xFFF0) == SC_KEYMENU)
            return;
        base.WndProc(ref m);
    }
}
