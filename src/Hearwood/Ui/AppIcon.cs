namespace Hearwood.Ui;

/// <summary>The app's icon (assets/Hearwood.ico, drawn by tools/make_icon.py), embedded in the exe.</summary>
public static class AppIcon
{
    const string Resource = "Hearwood.assets.Hearwood.ico";

    /// <summary>All sizes, for the window (title bar and Alt+Tab pick their own).</summary>
    public static Icon Window() => Load(null);

    /// <summary>The size the notification area shows, at the current DPI.</summary>
    public static Icon Tray() => Load(SystemInformation.SmallIconSize);

    /// <summary>A bitmap of the icon at <paramref name="pixels"/>, for drawing in the window.</summary>
    public static Bitmap Bitmap(int pixels)
    {
        using var icon = Load(new Size(pixels, pixels));
        return icon.ToBitmap();
    }

    static Icon Load(Size? size)
    {
        using var stream = typeof(AppIcon).Assembly.GetManifestResourceStream(Resource)
            ?? throw new InvalidOperationException($"The icon resource {Resource} is missing.");
        return size is { } s ? new Icon(stream, s) : new Icon(stream);
    }
}
