using System.Diagnostics;

namespace Hearwood;

/// <summary>
/// The licence files copied next to the exe (see Hearwood.csproj), opened from the tray menu.
/// The Store package's folder can't be browsed, so the menu is the only way to reach them there.
/// </summary>
static class Licences
{
    public static readonly (string Label, string File)[] Files =
    [
        ("Hearwood licence", "LICENSE.txt"),
        ("Third-party notices", "THIRD-PARTY-NOTICES.txt"),
        ("Rust crates in ptts_ffi.dll", "ptts_ffi-crate-licences.txt"),
    ];

    public static string PathOf(string file) => Path.Combine(AppContext.BaseDirectory, file);

    /// <summary>Opens the file in the user's text editor.</summary>
    public static void Open(string file) =>
        Process.Start(new ProcessStartInfo(PathOf(file)) { UseShellExecute = true })?.Dispose();
}
