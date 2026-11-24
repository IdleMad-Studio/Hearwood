using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Hearwood.Pocket;

/// <summary>
/// Turns Kyutai's Pocket TTS checkpoint into a folder the Phonon runtime (ptts) loads: its own
/// config and the weights without <c>flow_lm.bos_before_voice</c>. The app's voice files open with
/// that BOS frame instead (SPECS §4).
/// </summary>
public static class PocketCheckpoint
{
    /// <summary>The learnt frame pocket-tts puts before every voice prompt.</summary>
    public const string BosTensor = "flow_lm.bos_before_voice";

    /// <summary>The tensor a ptts voice file holds: the BOS frame, then the voice's frames, [1, frames + 1, 1024].</summary>
    public const string VoiceTensor = "emb";

    /// <summary>
    /// The config Kyutai does not ship: ptts's Pocket TTS 100M fixture (no <c>model_id</c>, so tanh
    /// GELU and pocket-tts's time norm), with the encoder's 32-wide latents (<c>inner_dim</c>) that
    /// <c>flow_lm.speaker_proj_weight</c> [1024, 32] expects.
    /// </summary>
    public const string ConfigJson = """
        {
          "languages": ["en"],
          "flow_lm": {
            "d_model": 1024,
            "num_heads": 16,
            "num_layers": 6,
            "dim_feedforward": 4096,
            "max_period": 10000.0,
            "n_bins": 4000,
            "lut_dim": 1024,
            "flow_dim": 512,
            "flow_depth": 6,
            "ldim": 32
          },
          "mimi": {
            "channels": 1,
            "sample_rate": 24000,
            "frame_rate": 12.5,
            "dimension": 512,
            "quantizer_dimension": 32,
            "quantizer_output_dimension": 512,
            "n_filters": 64,
            "n_residual_layers": 1,
            "ratios": [6, 5, 4],
            "kernel_size": 7,
            "last_kernel_size": 3,
            "residual_kernel_size": 3,
            "dilation_base": 2,
            "compress": 2,
            "transformer_d_model": 512,
            "transformer_num_heads": 8,
            "transformer_num_layers": 2,
            "transformer_layer_scale": 0.01,
            "transformer_context": 250,
            "transformer_max_period": 10000.0,
            "transformer_dim_feedforward": 2048,
            "downsample_channel_wise": false,
            "inner_dim": 32
          },
          "lsd_decode_steps": 1,
          "eos_threshold": -4.0,
          "model_id": null,
          "audio_prompt_min_duration": 10.0,
          "audio_prompt_max_duration": 10.0,
          "cfg_null_audio_empty": false,
          "speaker_mimi": null,
          "conditioners": [],
          "fuser": null,
          "voices": []
        }
        """;

    /// <summary>Writes <see cref="ConfigJson"/> unless the file already holds it.</summary>
    public static void WriteConfig(string path)
    {
        if (File.Exists(path) && File.ReadAllText(path) == ConfigJson)
            return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, ConfigJson);
    }

    /// <summary>
    /// Copies a safetensors file without <see cref="BosTensor"/>, which the pinned ptts refuses as
    /// unused, and returns that tensor as floats. Both outputs are written beside their targets first
    /// and moved into place, the weights last, so an interrupted run leaves no half file behind.
    /// </summary>
    public static float[] SplitOffBos(string sourcePath, string weightsPath, string bosPath)
    {
        using var source = File.OpenRead(sourcePath);
        var (header, dataStart) = ReadHeader(source, sourcePath);
        if (header[BosTensor] is not JsonObject bosEntry)
            throw new InvalidDataException($"'{Path.GetFileName(sourcePath)}' has no {BosTensor}.");
        header.Remove(BosTensor);

        var bos = ReadFloats(source, dataStart, bosEntry, sourcePath);

        // The other tensors keep their order and are packed again from offset 0.
        var tensors = header
            .Where(p => p.Key != "__metadata__")
            .Select(p => (p.Key, Entry: (JsonObject)p.Value!, Range: Offsets((JsonObject)p.Value!)))
            .OrderBy(t => t.Range.Start)
            .ToList();
        long next = 0;
        foreach (var (_, entry, range) in tensors)
        {
            long length = range.End - range.Start;
            entry["data_offsets"] = new JsonArray(next, next + length);
            next += length;
        }

        var bosTemp = bosPath + ".part";
        NumpyIO.Write(bosTemp, bos, [1, 1, bos.Length]);

        var weightsTemp = weightsPath + ".part";
        try
        {
            using (var target = new FileStream(weightsTemp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
            {
                WriteHeader(target, header);
                var buffer = new byte[1 << 20];
                foreach (var (_, _, range) in tensors)
                {
                    source.Position = dataStart + range.Start;
                    for (long left = range.End - range.Start; left > 0; )
                    {
                        int read = source.Read(buffer, 0, (int)Math.Min(buffer.Length, left));
                        if (read == 0)
                            throw new InvalidDataException($"'{Path.GetFileName(sourcePath)}' ends inside a tensor.");
                        target.Write(buffer, 0, read);
                        left -= read;
                    }
                }
            }
            File.Move(bosTemp, bosPath, overwrite: true);
            File.Move(weightsTemp, weightsPath, overwrite: true);
        }
        catch
        {
            File.Delete(weightsTemp);
            File.Delete(bosTemp);
            throw;
        }
        return bos;
    }

    /// <summary>Raw tensor bytes as floats (F32 or BF16).</summary>
    public static float[] DecodeFloats(byte[] bytes, string dtype) => dtype switch
    {
        "F32" => System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(bytes).ToArray(),
        // bfloat16 is the top half of a float32, so the conversion is exact.
        "BF16" => Enumerable.Range(0, bytes.Length / 2)
            .Select(i => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(2 * i)) << 16))
            .ToArray(),
        _ => throw new InvalidDataException($"Cannot read a {dtype} tensor."),
    };

    /// <summary>A safetensors file's tensor names, dtypes and shapes (tests and diagnostics).</summary>
    public static IReadOnlyDictionary<string, (string Dtype, long[] Shape)> ReadTensorInfo(string path)
    {
        using var stream = File.OpenRead(path);
        var (header, _) = ReadHeader(stream, path);
        return header
            .Where(p => p.Key != "__metadata__")
            .ToDictionary(
                p => p.Key,
                p => (p.Value!["dtype"]!.GetValue<string>(), p.Value!["shape"]!.AsArray().Select(d => d!.GetValue<long>()).ToArray()));
    }

    /// <summary>One tensor of a safetensors file as floats (F32 or BF16).</summary>
    public static float[] ReadTensor(string path, string name)
    {
        using var stream = File.OpenRead(path);
        var (header, dataStart) = ReadHeader(stream, path);
        if (header[name] is not JsonObject entry)
            throw new InvalidDataException($"'{Path.GetFileName(path)}' has no tensor '{name}'.");
        return ReadFloats(stream, dataStart, entry, path);
    }

    private static (JsonObject Header, long DataStart) ReadHeader(Stream stream, string path)
    {
        Span<byte> prefix = stackalloc byte[8];
        stream.ReadExactly(prefix);
        long length = BinaryPrimitives.ReadInt64LittleEndian(prefix);
        if (length <= 0 || length > 100_000_000 || 8 + length > stream.Length)
            throw new InvalidDataException($"'{Path.GetFileName(path)}' is not a safetensors file.");
        var json = new byte[length];
        stream.ReadExactly(json);
        var header = JsonNode.Parse(json) as JsonObject
            ?? throw new InvalidDataException($"'{Path.GetFileName(path)}' has no safetensors header.");
        return (header, 8 + length);
    }

    private static void WriteHeader(Stream stream, JsonObject header)
    {
        var json = Encoding.UTF8.GetBytes(header.ToJsonString());
        // The data must start 8-byte aligned; the format pads the header with spaces.
        int padded = (json.Length + 7) / 8 * 8;
        Span<byte> prefix = stackalloc byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(prefix, padded);
        stream.Write(prefix);
        stream.Write(json);
        for (int i = json.Length; i < padded; i++)
            stream.WriteByte((byte)' ');
    }

    private static (long Start, long End) Offsets(JsonObject entry)
    {
        var offsets = entry["data_offsets"]!.AsArray();
        return (offsets[0]!.GetValue<long>(), offsets[1]!.GetValue<long>());
    }

    private static float[] ReadFloats(Stream stream, long dataStart, JsonObject entry, string path)
    {
        var (start, end) = Offsets(entry);
        var bytes = new byte[end - start];
        stream.Position = dataStart + start;
        stream.ReadExactly(bytes);

        try
        {
            return DecodeFloats(bytes, entry["dtype"]!.GetValue<string>());
        }
        catch (InvalidDataException ex)
        {
            throw new InvalidDataException($"'{Path.GetFileName(path)}': {ex.Message}");
        }
    }
}
