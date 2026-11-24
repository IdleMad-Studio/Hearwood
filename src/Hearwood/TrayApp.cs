using Hearwood.Pocket;
using Hearwood.Ui;

namespace Hearwood;

/// <summary>
/// The tray icon, the global hotkeys and the window, around one Pocket engine and one player.
/// The read hotkey (Ctrl+Alt+S unless the user changes it) puts the selected text in the window's box and reads it
/// without bringing the window up. Every hotkey can be changed from the tray menu.
/// </summary>
public sealed class TrayApp : ApplicationContext
{
    readonly Config _cfg = Config.Load();
    readonly PocketService _pocket = new();
    readonly SpeechPlayer _player;
    readonly MainWindow _window;
    readonly NotifyIcon _tray;
    readonly HotkeyManager _hotkeys = new();
    readonly ToolStripMenuItem _voiceMenu = new("Voice");
    readonly ToolStripMenuItem _speedMenu = new("Speed");
    readonly ToolStripMenuItem _pauseItem = new();
    readonly ToolStripMenuItem _stopItem = new();
    readonly ToolStripMenuItem _hotkeysMenu = new(HotkeyDialog.ChangeLabel);
    readonly RegisteredWaitHandle _showWait;
    HotkeyDialog? _hotkeyDialog;
    SubstitutionsDialog? _substitutionsDialog;
    bool _exiting;

    public TrayApp(bool showWindow, EventWaitHandle showRequests)
    {
        _player = new SpeechPlayer(_pocket) { Rate = _cfg.Rate, Substitutions = LoadSubstitutions(out var substitutionsProblem) };
        _window = new MainWindow(_cfg, _pocket, _player);
        _ = _window.Handle; // so worker threads can post to it before it is first shown

        _player.Error += msg => Post(() => { _window.SetStatus(msg); Notify(msg, ToolTipIcon.Error); });
        _player.StateChanged += () => Post(() =>
        {
            _pauseItem.Enabled = _player.IsSpeaking;
            ShowPauseItem();
        });
        _window.VoiceChosen += SetVoice;
        _window.RateChosen += SetRate;
        _window.TextSizeChosen += SetTextSize;
        _window.SubstitutionsChosen += OpenSubstitutions;
        _window.HotkeyChangeChosen += ChangeHotkey;

        _pauseItem.Enabled = false;
        _pauseItem.Click += (_, _) => _player.TogglePause();
        var menu = new ContextMenuStrip();
        menu.Items.Add("Show window", null, (_, _) => _window.ShowAndActivate());
        // Clicking the tray takes focus from the selection, so the menu reads the clipboard instead.
        menu.Items.Add("Speak clipboard", null, (_, _) => SpeakClipboard());
        menu.Items.Add(_pauseItem);
        _stopItem.Click += (_, _) => _player.Stop();
        menu.Items.Add(_stopItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_voiceMenu);
        menu.Items.Add(_speedMenu);
        menu.Items.Add(SubstitutionsLabel + "…", null, (_, _) => OpenSubstitutions(null));
        menu.Items.Add(_hotkeysMenu);
        menu.Items.Add(new ToolStripSeparator());
        var startup = new ToolStripMenuItem("Start with Windows") { Checked = _cfg.StartWithWindows };
        startup.Click += async (_, _) => await SetStartWithWindowsAsync(startup);
        menu.Items.Add(startup);
        var licences = new ToolStripMenuItem("Licences");
        foreach (var (label, file) in Licences.Files)
            licences.DropDownItems.Add(label, null, (_, _) => OpenLicence(file));
        menu.Items.Add(licences);
        menu.Items.Add("Quit", null, (_, _) => ExitThread());
        menu.Opening += async (_, _) =>
        {
            BuildSpeedMenu();
            BuildVoiceMenu();
            BuildHotkeysMenu();
            // The Store package's startup task can also be switched in Windows' settings.
            await ShowStartWithWindowsAsync(startup);
        };

        // An entry written by a copy of the app in another folder would start that copy.
        if (_cfg.StartWithWindows)
            StartupRegistration.Refresh();

        _tray = new NotifyIcon { Icon = AppIcon.Tray(), Text = "Hearwood", ContextMenuStrip = menu, Visible = true };
        _tray.DoubleClick += (_, _) => _window.ShowAndActivate();

        RegisterHotkeys();
        if (substitutionsProblem is not null)
            Notify(substitutionsProblem, ToolTipIcon.Warning);

        // A second launch asks for the window instead of starting another copy.
        _showWait = ThreadPool.RegisterWaitForSingleObject(showRequests, (_, _) => Post(_window.ShowAndActivate), null, -1, false);

        _pocket.Progress += (status, _) => Post(() =>
        {
            _window.SetStatus(status);
            _tray.Text = status.Length <= 63 ? status : status[..63];
        });
        if (showWindow)
            _window.ShowAndActivate();
        _ = WarmAsync();
    }

    void RegisterHotkeys()
    {
        var failed = new List<string>();
        void Add(Hotkey hotkey, Action action)
        {
            if (!_hotkeys.Register(hotkey, action)) failed.Add(hotkey.ToString());
        }
        foreach (var action in HotkeyActions.All)
            Add(_cfg.GetHotkey(action), action switch
            {
                HotkeyAction.Read => async () => await SpeakSelectionAsync(),
                HotkeyAction.Pause => _player.TogglePause,
                HotkeyAction.Stop => _player.Stop,
                HotkeyAction.Faster => () => SetRate(Config.StepRate(_cfg.Rate, +1)),
                HotkeyAction.Slower => () => SetRate(Config.StepRate(_cfg.Rate, -1)),
                _ => throw new ArgumentOutOfRangeException(nameof(action)),
            });
        if (failed.Count > 0)
            Notify("Hotkey already in use: " + string.Join(", ", failed), ToolTipIcon.Warning);
    }

    void ChangeHotkey(HotkeyAction action)
    {
        if (_hotkeyDialog is not null) { _hotkeyDialog.Activate(); return; }

        // Off while the dialog is open, so a combination pressed there is shown instead of acted on.
        _hotkeys.UnregisterAll();
        var taken = new Dictionary<Hotkey, HotkeyAction>();
        foreach (var other in HotkeyActions.All.Where(a => a != action))
            taken.TryAdd(_cfg.GetHotkey(other), other);
        var dialog = new HotkeyDialog(action, _cfg.GetHotkey(action), taken, _hotkeys.IsFree);
        _hotkeyDialog = dialog;
        try { dialog.ShowDialog(); }
        finally { _hotkeyDialog = null; dialog.Dispose(); }
        if (_exiting) return;

        if (dialog.Chosen is { } chosen)
        {
            _cfg.SetHotkey(action, chosen);
            _window.ShowReadHotkey(_cfg.GetHotkey(HotkeyAction.Read));
            TrySave();
        }
        RegisterHotkeys();
    }

    /// <summary>The label of the tray item and the window's button that open the substitutions.</summary>
    public const string SubstitutionsLabel = "Word Substitutions";

    /// <summary>
    /// The saved substitutions. A file that can't be read is renamed and none are used, so the next save doesn't
    /// overwrite the list (perhaps a hand edit that went wrong); <paramref name="problem"/> says so.
    /// </summary>
    static Text.Substitutions LoadSubstitutions(out string? problem)
    {
        problem = null;
        try { return Text.Substitutions.Load(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            var path = Text.Substitutions.PathOnDisk;
            try
            {
                File.Move(path, path + ".unreadable", overwrite: true);
                problem = $"The word substitutions couldn't be read, so they are off. The file was kept as {Path.GetFileName(path)}.unreadable.";
            }
            catch (Exception) { problem = "The word substitutions couldn't be read, so they are off: " + e.Message; }
            return Text.Substitutions.Empty;
        }
    }

    /// <summary>Opens the substitutions window, or brings it up; <paramref name="word"/> starts an entry for that word.</summary>
    void OpenSubstitutions(string? word)
    {
        if (_substitutionsDialog is null)
        {
            _substitutionsDialog = new SubstitutionsDialog(_player.Substitutions.Rules, SaveSubstitutions,
                text => _player.Speak(text, _window.Voice, substitute: false));
            _substitutionsDialog.FormClosed += (_, _) => { _substitutionsDialog.Dispose(); _substitutionsDialog = null; };
            _substitutionsDialog.Show();
        }
        if (word is not null)
            _substitutionsDialog.Prefill(word);
        if (_substitutionsDialog.WindowState == FormWindowState.Minimized)
            _substitutionsDialog.WindowState = FormWindowState.Normal;
        _substitutionsDialog.Activate();
    }

    bool SaveSubstitutions(IReadOnlyList<Text.SubstitutionRule> rules)
    {
        try
        {
            Text.Substitutions.Write(Text.Substitutions.PathOnDisk, rules);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(_substitutionsDialog, "Couldn't save the substitutions.\n\n" + e.Message, "Hearwood", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        _player.Substitutions = new Text.Substitutions(rules);
        return true;
    }

    async Task WarmAsync()
    {
        try
        {
            _window.SetStatus("Loading Pocket…");
            await _pocket.ReadyAsync(_window.Voice);
            _window.SetStatus("Ready.");
        }
        catch (Exception e)
        {
            _window.SetStatus(e.Message);
            Notify(e.Message, ToolTipIcon.Error);
        }
        _tray.Text = "Hearwood";
    }

    async Task SpeakSelectionAsync()
    {
        var text = await SelectionGrabber.GrabAsync();
        if (string.IsNullOrWhiteSpace(text)) { Notify("Nothing to read.", ToolTipIcon.Info); return; }
        _window.SpeakInBox(text);
    }

    void SpeakClipboard()
    {
        var text = Clipboard.ContainsText() ? Clipboard.GetText() : null;
        if (string.IsNullOrWhiteSpace(text)) { Notify("The clipboard has no text.", ToolTipIcon.Info); return; }
        _window.SpeakInBox(text);
    }

    void SetVoice(string voice)
    {
        _cfg.Voice = voice;
        _window.RefreshVoices();
        TrySave();
    }

    void SetRate(double rate)
    {
        _cfg.Rate = Config.SnapRate(rate);
        _player.Rate = _cfg.Rate; // heard within a fraction of a second
        _window.RefreshSpeed();
        TrySave();
    }

    void SetTextSize(float size)
    {
        size = Config.SnapTextSize(size);
        if (size == _cfg.TextSize) return;
        _cfg.TextSize = size;
        _window.RefreshTextSize();
        TrySave();
    }

    async Task SetStartWithWindowsAsync(ToolStripMenuItem item)
    {
        try
        {
            bool on = await StartupRegistration.SetAsync(!item.Checked);
            _cfg.StartWithWindows = on;
            item.Checked = on;
            TrySave();
        }
        catch (Exception e)
        {
            Notify("Could not change the startup setting: " + e.Message, ToolTipIcon.Warning);
        }
    }

    async Task ShowStartWithWindowsAsync(ToolStripMenuItem item)
    {
        try { item.Checked = await StartupRegistration.IsOnAsync(_cfg.StartWithWindows); }
        catch (Exception) { /* keep what the menu shows */ }
    }

    void BuildSpeedMenu()
    {
        _speedMenu.DropDownItems.Clear();
        foreach (var s in Config.SpeedChoices)
        {
            var item = new ToolStripMenuItem($"{s:0.##}×") { Checked = Math.Abs(s - _cfg.Rate) < 1e-9 };
            item.Click += (_, _) => SetRate(s);
            _speedMenu.DropDownItems.Add(item);
        }
    }

    void BuildHotkeysMenu()
    {
        _stopItem.Text = $"Stop ({_cfg.GetHotkey(HotkeyAction.Stop)})";
        ShowPauseItem();
        _hotkeysMenu.DropDownItems.Clear();
        _hotkeysMenu.DropDownItems.AddRange(HotkeyDialog.MenuItems(_cfg, ChangeHotkey));
    }

    void ShowPauseItem() =>
        _pauseItem.Text = $"{(_player.IsPaused ? "Resume" : "Pause")} ({_cfg.GetHotkey(HotkeyAction.Pause)})";

    void BuildVoiceMenu()
    {
        _voiceMenu.DropDownItems.Clear();
        foreach (var v in _pocket.Voices.List())
        {
            var item = new ToolStripMenuItem(VoiceLibrary.Label(v)) { Checked = v == _window.Voice };
            item.Click += (_, _) => SetVoice(v);
            _voiceMenu.DropDownItems.Add(item);
        }
    }

    void OpenLicence(string file)
    {
        try { Licences.Open(file); }
        catch (Exception e) { Notify($"Could not open {file}: {e.Message}", ToolTipIcon.Warning); }
    }

    void TrySave()
    {
        try { _cfg.Save(); } catch (Exception e) { Notify("Could not save settings: " + e.Message, ToolTipIcon.Warning); }
    }

    void Post(Action action)
    {
        if (_window.IsDisposed) return;
        try { _window.BeginInvoke(action); } catch (InvalidOperationException) { }
    }

    void Notify(string text, ToolTipIcon icon)
    {
        _tray.BalloonTipTitle = "Hearwood";
        _tray.BalloonTipText = text;
        _tray.BalloonTipIcon = icon;
        _tray.ShowBalloonTip(3000);
    }

    protected override void ExitThreadCore()
    {
        _exiting = true;
        _hotkeyDialog?.Close();
        _substitutionsDialog?.Close();
        _showWait.Unregister(null);
        _player.Stop();
        _hotkeys.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        _window.Dispose();
        _pocket.Dispose();
        base.ExitThreadCore();
    }
}
