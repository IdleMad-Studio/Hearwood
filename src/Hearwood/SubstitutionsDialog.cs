using Hearwood.Text;
using Hearwood.Ui;

namespace Hearwood;

/// <summary>
/// The word substitutions window, like the one in another TTS app by the same author: the list, a form to add or
/// change one, buttons to hear the word and its substitute, and import and export of ListenEasy files. Every change
/// is saved at once, through the callback, and applies from the next thing spoken.
/// </summary>
public sealed class SubstitutionsDialog : Form
{
    readonly Theme _theme = Theme.Current;
    readonly Func<IReadOnlyList<SubstitutionRule>, bool> _save;
    readonly Action<string> _listen;
    List<SubstitutionRule> _rules;

    readonly ListView _list = new()
    {
        Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false,
        HeaderStyle = ColumnHeaderStyle.Nonclickable, BorderStyle = BorderStyle.None,
    };
    readonly TextBox _word = new() { Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle };
    readonly TextBox _say = new() { Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle };
    readonly CheckBox _case = new() { Text = "Match case", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(12, 0, 0, 0) };
    readonly Label _count = new() { AutoSize = true, Anchor = AnchorStyles.Right, Margin = new Padding(0, 0, 12, 0) };

    /// <param name="rules">The saved rules.</param>
    /// <param name="save">Saves and applies a changed list; false when it couldn't be saved (and the user has been told).</param>
    /// <param name="listen">Speaks a piece of text as typed, without substitutions.</param>
    public SubstitutionsDialog(IReadOnlyList<SubstitutionRule> rules, Func<IReadOnlyList<SubstitutionRule>, bool> save, Action<string> listen)
    {
        _rules = [.. rules];
        _save = save;
        _listen = listen;
        // Sizes below are at 100% scale; WinForms scales them to the monitor's DPI when layout resumes (see MainWindow).
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96f, 96f);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "Word substitutions";
        Icon = AppIcon.Window();
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(640, 500);
        MinimumSize = new Size(560, 400);
        MinimizeBox = false;
        BackColor = _theme.Window;
        ForeColor = _theme.Text;
        Font = Theme.UiFont(10f);

        var intro = new Label
        {
            Text = "Words the voice says wrong, and what it should say instead. Changes are saved straight away.",
            AutoSize = true, MaximumSize = new Size(600, 0), Margin = new Padding(0, 0, 0, 10),
        };

        _list.BackColor = _theme.Surface;
        _list.ForeColor = _theme.Text;
        _list.Columns.Add("Word");
        _list.Columns.Add("Say as");
        _list.Columns.Add("Match case");
        _list.SizeChanged += (_, _) => FitColumns();
        _list.SelectedIndexChanged += (_, _) => ShowSelected();
        _list.KeyDown += (_, e) => { if (e.KeyCode == Keys.Delete) Delete(); };
        var card = new Card(_theme) { Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = new Padding(10, 10, 4, 10) };
        card.Controls.Add(_list);

        foreach (var box in new[] { _word, _say })
        {
            box.BackColor = _theme.Surface;
            box.ForeColor = _theme.Text;
            box.Margin = new Padding(0, 0, 8, 0);
        }
        var form = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 3, RowCount = 2, Margin = new Padding(0, 14, 0, 0) };
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        form.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        form.Controls.Add(Caption("Word"), 0, 0);
        form.Controls.Add(Caption("Say as"), 1, 0);
        form.Controls.Add(_word, 0, 1);
        form.Controls.Add(_say, 1, 1);
        form.Controls.Add(_case, 2, 1);

        var add = new PillButton(_theme, primary: true) { Text = "Add / Update" };
        add.Click += (_, _) => Add();
        AcceptButton = add;
        var delete = new PillButton(_theme) { Glyph = Glyph.Clear, Text = "Delete" };
        delete.Click += (_, _) => Delete();
        var listenWord = new PillButton(_theme) { Glyph = Glyph.Play, Text = "Hear word" };
        listenWord.Click += (_, _) => Listen(_word.Text);
        var listenSay = new PillButton(_theme) { Glyph = Glyph.Play, Text = "Hear substitute", Margin = Padding.Empty };
        listenSay.Click += (_, _) => Listen(_say.Text);
        var actions = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Dock = DockStyle.Fill, Margin = new Padding(0, 10, 0, 0) };
        actions.Controls.AddRange([add, delete, listenWord, listenSay]);

        var import = new PillButton(_theme) { Text = "Import…" };
        import.Click += (_, _) => Import();
        var export = new PillButton(_theme) { Text = "Export…" };
        export.Click += (_, _) => Export();
        var close = new PillButton(_theme) { Text = "Close", Margin = Padding.Empty };
        close.Click += (_, _) => Close();
        CancelButton = close;
        _count.ForeColor = _theme.Muted;
        _count.Font = Theme.UiFont(9f);
        var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 5, Margin = new Padding(0, 16, 0, 0) };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottom.Controls.Add(import, 0, 0);
        bottom.Controls.Add(export, 1, 0);
        bottom.Controls.Add(_count, 3, 0);
        bottom.Controls.Add(close, 4, 0);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(22, 18, 22, 16), BackColor = _theme.Window };
        // One column, the window's width: left to size itself, it grows to the widest row and runs off the edge.
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(intro, 0, 0);
        root.Controls.Add(card, 0, 1);
        root.Controls.Add(form, 0, 2);
        root.Controls.Add(actions, 0, 3);
        root.Controls.Add(bottom, 0, 4);
        Controls.Add(root);
        ResumeLayout(false);
        PerformLayout();

        Refill();
        ActiveControl = _word;
    }

    Label Caption(string text) => new()
    {
        Text = text, AutoSize = true, ForeColor = _theme.Muted, Font = Theme.UiFont(9f), Margin = new Padding(0, 0, 0, 4),
    };

    /// <summary>Starts an entry for <paramref name="word"/>, or selects its rule if it has one.</summary>
    public void Prefill(string word)
    {
        int at = _rules.FindIndex(r => string.Equals(r.From, word, StringComparison.OrdinalIgnoreCase));
        if (at >= 0)
        {
            SelectRule(at);
            ActiveControl = _say;
            return;
        }
        _list.SelectedIndices.Clear();
        _word.Text = word;
        _say.Text = "";
        _case.Checked = false;
        ActiveControl = _say;
    }

    void FitColumns()
    {
        int caseWidth = LogicalToDeviceUnits(96);
        int rest = Math.Max(0, _list.ClientSize.Width - caseWidth);
        _list.Columns[0].Width = rest / 2;
        _list.Columns[1].Width = rest - rest / 2;
        _list.Columns[2].Width = caseWidth;
    }

    void Refill(int? select = null)
    {
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var r in _rules)
            _list.Items.Add(new ListViewItem([r.From, r.To, r.MatchCase ? "Yes" : ""]));
        _list.EndUpdate();
        if (select is { } i && i >= 0 && i < _rules.Count)
            SelectRule(i);
        _count.Text = _rules.Count == 1 ? "1 substitution" : $"{_rules.Count} substitutions";
    }

    void SelectRule(int index)
    {
        _list.SelectedIndices.Clear();
        _list.SelectedIndices.Add(index);
        _list.EnsureVisible(index);
    }

    int? Selected => _list.SelectedIndices.Count > 0 ? _list.SelectedIndices[0] : null;

    void ShowSelected()
    {
        if (Selected is not { } i)
            return;
        var r = _rules[i];
        _word.Text = r.From;
        _say.Text = r.To;
        _case.Checked = r.MatchCase;
    }

    void Add()
    {
        var word = _word.Text.Trim();
        if (word.Length == 0)
            return;
        var rule = new SubstitutionRule(word, _say.Text.Trim(), _case.Checked);
        var rules = new List<SubstitutionRule>(_rules);
        int at = rules.FindIndex(r => r.From == word);
        if (at < 0)
        {
            rules.Add(rule);
            at = rules.Count - 1;
        }
        else
            rules[at] = rule;
        if (Commit(rules))
            Refill(at);
    }

    void Delete()
    {
        if (Selected is not { } i)
            return;
        var rules = new List<SubstitutionRule>(_rules);
        rules.RemoveAt(i);
        if (!Commit(rules))
            return;
        Refill(rules.Count > 0 ? Math.Min(i, rules.Count - 1) : null);
        if (rules.Count == 0)
        {
            _word.Text = _say.Text = "";
            _case.Checked = false;
        }
    }

    /// <summary>Saves <paramref name="rules"/> and shows them; the list stays as it was when they can't be saved.</summary>
    bool Commit(List<SubstitutionRule> rules)
    {
        if (!_save(rules))
            return false;
        _rules = rules;
        return true;
    }

    void Listen(string text)
    {
        if (!string.IsNullOrWhiteSpace(text))
            _listen(text.Trim());
    }

    void Import()
    {
        using var dialog = new OpenFileDialog { Title = "Import substitutions", Filter = "Substitutions (*.json)|*.json|All files (*.*)|*.*" };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        IReadOnlyList<SubstitutionRule> imported;
        try { imported = Substitutions.Read(dialog.FileName); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            MessageBox.Show(this, "Couldn't import that file.\n\n" + e.Message, "Hearwood", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        bool replace = true;
        if (_rules.Count > 0)
        {
            var answer = MessageBox.Show(this,
                $"Importing {imported.Count} substitutions.\n\nYes: replace the current list.\nNo: merge them into it (imported entries win).",
                "Import substitutions", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (answer == DialogResult.Cancel)
                return;
            replace = answer == DialogResult.Yes;
        }
        List<SubstitutionRule> rules = replace ? [.. imported] : Merge(_rules, imported);
        if (Commit(rules))
            Refill();
    }

    /// <summary><paramref name="current"/> with <paramref name="imported"/> added; an imported rule replaces one for the same word in place.</summary>
    internal static List<SubstitutionRule> Merge(IReadOnlyList<SubstitutionRule> current, IReadOnlyList<SubstitutionRule> imported)
    {
        var rules = new List<SubstitutionRule>(current);
        foreach (var rule in imported)
        {
            int at = rules.FindIndex(r => r.From == rule.From);
            if (at >= 0) rules[at] = rule;
            else rules.Add(rule);
        }
        return rules;
    }

    void Export()
    {
        using var dialog = new SaveFileDialog
        {
            Title = "Export substitutions", Filter = "Substitutions (*.json)|*.json", DefaultExt = "json", FileName = "hearwood-substitutions.json",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        try { Substitutions.Write(dialog.FileName, _rules); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, "Couldn't export.\n\n" + e.Message, "Hearwood", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
