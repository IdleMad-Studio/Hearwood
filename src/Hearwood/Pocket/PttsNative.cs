using System.Runtime.InteropServices;

namespace Hearwood.Pocket;

/// <summary>
/// Declarations from ptts.h, the C library of Gradium's Phonon runtime (xn-ptts), built by
/// installer/build-phonon.ps1. C <c>bool</c> is one byte, unlike the C# marshalling default.
/// </summary>
internal static class PttsNative
{
    public const string Library = "ptts_ffi";
    public const string LibraryFile = Library + ".dll";

    public const uint CpuUnit = 0;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public delegate bool FrameCallback(nint pcm, nuint count, nint user);

    [StructLayout(LayoutKind.Sequential)]
    public struct Result
    {
        public uint Frames;
        public nuint Samples;
        public double TimeToFirstAudioMs;
        public double TotalMs;
    }

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    public static extern nint ptts_new([MarshalAs(UnmanagedType.LPUTF8Str)] string directory, uint unit, [MarshalAs(UnmanagedType.LPUTF8Str)] string language);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool ptts_speak(nint handle, [MarshalAs(UnmanagedType.LPUTF8Str)] string text, FrameCallback callback, nint user, out Result result);

    /// <summary>Voice names, sorted, as one NUL-separated block ending in a second NUL. Borrowed.</summary>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    public static extern nint ptts_voices(nint handle);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool ptts_set_voice(nint handle, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    public static extern void ptts_set_seed(nint handle, ulong seed);

    /// <summary>Applies from the next ptts_speak on; false for a negative, infinite or NaN value. 0 removes the randomness.</summary>
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool ptts_set_temperature(nint handle, float temperature);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    public static extern nint ptts_last_error(nint handle);

    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
    public static extern void ptts_free(nint handle);

    public static string LastError(nint handle) =>
        Marshal.PtrToStringUTF8(ptts_last_error(handle)) ?? "unknown error";

    /// <summary>The names in a NUL-separated block ending in a second NUL.</summary>
    public static IReadOnlyList<string> ReadNames(nint block)
    {
        var names = new List<string>();
        if (block == 0)
            return names;
        for (int offset = 0; ; )
        {
            var name = Marshal.PtrToStringUTF8(block + offset);
            if (string.IsNullOrEmpty(name))
                return names;
            names.Add(name);
            offset += System.Text.Encoding.UTF8.GetByteCount(name) + 1;
        }
    }
}
