using System.Text;

namespace Hearwood.Pocket;

/// <summary>Minimal .npy float32 reader/writer (Pocket's cached voice embeddings and BOS frame).</summary>
public static class NumpyIO
{
    /// <summary>Reads a float32 .npy with any dimension count, flattened row-major.</summary>
    public static (float[] Data, int[] Shape) Read(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);

        var magic = reader.ReadBytes(6);
        if (magic[0] != 0x93 || magic[1] != (byte)'N' || magic[2] != (byte)'U')
            throw new InvalidDataException($"'{path}' is not an .npy file.");

        int major = reader.ReadByte(), minor = reader.ReadByte();
        int headerLength = major == 1 ? reader.ReadUInt16() : (int)reader.ReadUInt32();
        var header = Encoding.ASCII.GetString(reader.ReadBytes(headerLength));
        if (minor < 0)
            throw new InvalidDataException("Malformed .npy header.");

        var descr = ExtractHeader(header, "descr");
        var shapeTuple = ExtractHeader(header, "shape");
        if (descr != "'<f4'")
            throw new InvalidDataException($"Unsupported dtype {descr} (only float32 .npy is supported).");

        int[] shape = shapeTuple.Trim('(', ')')
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(int.Parse)
            .ToArray();

        var data = new float[(int)(stream.Length - stream.Position) / 4];
        for (int i = 0; i < data.Length; i++)
            data[i] = reader.ReadSingle();
        return (data, shape);
    }

    /// <summary>Writes a float32 .npy with the given shape (row-major, 16-byte-aligned header).</summary>
    public static void Write(string path, float[] data, int[] shape)
    {
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        writer.Write((byte)0x93);
        writer.Write("NUMPY"u8.ToArray());
        writer.Write((byte)1);
        writer.Write((byte)0);

        var headerBody = $"{{'descr': '<f4', 'fortran_order': False, 'shape': ({string.Join(", ", shape)}{(shape.Length == 1 ? "," : "")}), }}";
        // Numpy requires ≥16-byte alignment of payload; pad the dict text to fit.
        int unpadded = 10 + headerBody.Length + 1;
        int pad = 16 - (unpadded % 16);
        var header = Encoding.ASCII.GetBytes(headerBody + new string(' ', pad - 1) + "\n");
        writer.Write((short)header.Length);
        writer.Write(header);
        foreach (var value in data)
            writer.Write(value);
    }

    private static string ExtractHeader(string header, string key)
    {
        var needle = $"'{key}': ";
        var at = header.IndexOf(needle, StringComparison.Ordinal);
        if (at < 0)
            throw new InvalidDataException($"Missing '{key}' in .npy header.");
        at += needle.Length;

        // Tuple values (shape) extend to the matching ')'; scalars/quoted strings end at ','.
        if (header[at] == '(')
        {
            var close = header.IndexOf(')', at);
            if (close < 0)
                throw new InvalidDataException($"Malformed '{key}' tuple in .npy header.");
            return header[at..(close + 1)];
        }
        var end = header.IndexOf(',', at);
        return header[at..(end < 0 ? header.IndexOf('}', at) : end)].Trim().TrimEnd(',');
    }
}
