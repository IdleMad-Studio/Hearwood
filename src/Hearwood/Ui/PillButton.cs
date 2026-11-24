using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace Hearwood.Ui;

/// <summary>
/// A rounded, flat button with an optional icon-font glyph; the primary one is filled with the accent colour.
/// It sizes itself to its glyph and text at the current DPI (and to <see cref="WidthFor"/>, so a label change does
/// not move the row), with <see cref="BaseHeight"/> as its height at 100% scale.
/// </summary>
public sealed class PillButton : Button
{
    const TextFormatFlags TextFlags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter;

    readonly Theme _theme;
    bool _hover;
    bool _pressed;
    string _glyph = "";
    int _baseHeight = 38;
    string[] _widthFor = [];

    public PillButton(Theme theme, bool primary = false)
    {
        _theme = theme;
        Primary = primary;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Cursor = Cursors.Hand;
        Font = Theme.UiFont(10f, primary ? FontStyle.Bold : FontStyle.Regular);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(14, 0, 14, 0);
        Margin = new Padding(0, 0, 8, 0);
    }

    public bool Primary { get; }

    /// <summary>The height at 100% scale; the button is that much taller at higher DPI.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int BaseHeight
    {
        get => _baseHeight;
        set { _baseHeight = value; PerformLayoutAfterChange(); }
    }

    /// <summary>Other labels the button will show, so it is wide enough for each of them from the start.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string[] WidthFor
    {
        get => _widthFor;
        set { _widthFor = value; PerformLayoutAfterChange(); }
    }

    /// <summary>A glyph of the Windows icon font, drawn before the text (or alone when the text is empty).</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string Glyph
    {
        get => _glyph;
        set { _glyph = value; PerformLayoutAfterChange(); }
    }

    void PerformLayoutAfterChange()
    {
        Invalidate();
        // Only a size change needs a new layout, and that only reaches the parent through AutoSize.
        Parent?.PerformLayout(this, nameof(Size));
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _pressed = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

    public override Size GetPreferredSize(Size proposedSize)
    {
        int content = _widthFor.Append(Text).Max(t => ContentWidths(t, null).Total);
        return new Size(Padding.Horizontal + content, LogicalToDeviceUnits(_baseHeight));
    }

    /// <summary>The glyph, the gap after it and the text, in pixels, as <see cref="OnPaint"/> draws them.</summary>
    (int Glyph, int Gap, int Text, int Total) ContentWidths(string text, Graphics? g)
    {
        using var iconFont = Theme.IconFont(Font.Size);
        int glyph = _glyph.Length > 0 ? Measure(g, _glyph, iconFont) : 0;
        int gap = _glyph.Length > 0 && text.Length > 0 ? LogicalToDeviceUnits(8) : 0;
        int width = text.Length > 0 ? Measure(g, text, Font) : 0;
        return (glyph, gap, width, glyph + gap + width);
    }

    static int Measure(Graphics? g, string text, Font font) =>
        (g is null ? TextRenderer.MeasureText(text, font, Size.Empty, TextFlags) : TextRenderer.MeasureText(g, text, font, Size.Empty, TextFlags)).Width;

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? _theme.Window);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        Color fill, ink;
        if (Primary)
        {
            fill = !Enabled ? Blend(_theme.Accent, _theme.Window, 0.55f) : _pressed ? _theme.AccentPressed : _hover ? _theme.AccentHover : _theme.Accent;
            ink = _theme.OnAccent;
        }
        else
        {
            fill = _pressed && Enabled ? _theme.ControlPressed : _hover && Enabled ? _theme.ControlHover : _theme.Control;
            ink = Enabled ? _theme.Text : _theme.Muted;
        }

        float scale = DeviceDpi / 96f;
        var rect = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using (var path = Rounded(rect, 9 * scale))
        using (var brush = new SolidBrush(fill))
            g.FillPath(brush, path);

        if (Focused && ShowFocusCues)
        {
            var ring = RectangleF.Inflate(rect, -2.5f * scale, -2.5f * scale);
            using var path = Rounded(ring, 7 * scale);
            using var pen = new Pen(Primary ? _theme.OnAccent : _theme.Accent, 1.5f * scale);
            g.DrawPath(pen, path);
        }

        // Glyph and text centred together.
        using var iconFont = Theme.IconFont(Font.Size);
        var flags = TextFlags;
        var (glyphWidth, gap, textWidth, _) = ContentWidths(Text, g);
        int x = (Width - glyphWidth - gap - textWidth) / 2;
        if (glyphWidth > 0)
            TextRenderer.DrawText(g, _glyph, iconFont, new Rectangle(x, 0, glyphWidth, Height), ink, flags);
        if (textWidth > 0)
            TextRenderer.DrawText(g, Text, Font, new Rectangle(x + glyphWidth + gap, 0, textWidth + 2, Height), ink, flags);
    }

    internal static GraphicsPath Rounded(RectangleF r, float radius)
    {
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    static Color Blend(Color a, Color b, float t) => Color.FromArgb(
        (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
}

/// <summary>A rounded card with a hairline border, holding one child (the text box) inside its padding.</summary>
public sealed class Card : Panel
{
    readonly Theme _theme;

    public Card(Theme theme)
    {
        _theme = theme;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = theme.Surface;
        Padding = new Padding(18, 14, 8, 14);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? _theme.Window);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float scale = DeviceDpi / 96f;
        var rect = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using var path = PillButton.Rounded(rect, 12 * scale);
        using (var brush = new SolidBrush(_theme.Surface))
            g.FillPath(brush, path);
        using var pen = new Pen(_theme.Border, 1f);
        g.DrawPath(pen, path);
    }
}
