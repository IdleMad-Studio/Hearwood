using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Hearwood.Pocket;

namespace Hearwood.Tests;

/// <summary>Pocket on the Phonon runtime: the checkpoint folder ptts loads and its voice files. Ported from another project by the same author.</summary>
public sealed class PocketRuntimeTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hearwood-pocket-" + Guid.NewGuid().ToString("N"));

    public PocketRuntimeTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    /// <summary>A safetensors file as Kyutai's: tensors in data order, the BOS frame in BF16 first.</summary>
    private string WriteCheckpoint(float[] bos, params (string Name, float[] Values)[] others)
    {
        var header = new Dictionary<string, object>();
        var data = new MemoryStream();
        var bf16 = new byte[bos.Length * 2];
        for (int i = 0; i < bos.Length; i++)
            BinaryPrimitives.WriteUInt16LittleEndian(bf16.AsSpan(2 * i), (ushort)(BitConverter.SingleToInt32Bits(bos[i]) >> 16));
        header[PocketCheckpoint.BosTensor] = new { dtype = "BF16", shape = new[] { 1, 1, bos.Length }, data_offsets = new long[] { 0, bf16.Length } };
        data.Write(bf16);
        foreach (var (name, values) in others)
        {
            var bytes = MemoryMarshal.AsBytes(values.AsSpan()).ToArray();
            header[name] = new { dtype = "F32", shape = new[] { values.Length }, data_offsets = new long[] { data.Length, data.Length + bytes.Length } };
            data.Write(bytes);
        }

        var json = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(header));
        var path = Path.Combine(_dir, "kyutai.safetensors");
        using var file = File.Create(path);
        file.Write(BitConverter.GetBytes((long)json.Length));
        file.Write(json);
        data.Position = 0;
        data.CopyTo(file);
        return path;
    }

    [Fact]
    public void The_bos_frame_is_split_off_and_every_other_tensor_kept_intact()
    {
        // Values exact in bfloat16, as the real frame is.
        float[] bos = [0.5f, -1.25f, 3f, 0f];
        float[] a = [1f, 2f, 3f], b = [-4.5f];
        var source = WriteCheckpoint(bos, ("flow_lm.a", a), ("mimi.b", b));
        var weights = Path.Combine(_dir, "model.safetensors");
        var bosPath = Path.Combine(_dir, "bos.npy");

        var split = PocketCheckpoint.SplitOffBos(source, weights, bosPath);

        Assert.Equal(bos, split);
        var (saved, shape) = NumpyIO.Read(bosPath);
        Assert.Equal(bos, saved);
        Assert.Equal([1, 1, 4], shape);
        var tensors = PocketCheckpoint.ReadTensorInfo(weights);
        Assert.Equal(["flow_lm.a", "mimi.b"], tensors.Keys.Order());
        Assert.Equal(a, PocketCheckpoint.ReadTensor(weights, "flow_lm.a"));
        Assert.Equal(b, PocketCheckpoint.ReadTensor(weights, "mimi.b"));
        Assert.Empty(Directory.GetFiles(_dir, "*.part"));
    }

    [Fact]
    public void The_weights_without_their_data_start_aligned()
    {
        var source = WriteCheckpoint([1f], ("x", [2f]));
        var weights = Path.Combine(_dir, "model.safetensors");

        PocketCheckpoint.SplitOffBos(source, weights, Path.Combine(_dir, "bos.npy"));

        Assert.Equal(0, BitConverter.ToInt64(File.ReadAllBytes(weights), 0) % 8);
    }

    [Fact]
    public void The_config_is_pocket_100m_with_the_32_wide_speaker_latents()
    {
        var path = Path.Combine(_dir, "config.json");
        PocketCheckpoint.WriteConfig(path);

        using var config = JsonDocument.Parse(File.ReadAllText(path));
        var root = config.RootElement;
        Assert.Equal(1024, root.GetProperty("flow_lm").GetProperty("d_model").GetInt32());
        Assert.Equal(32, root.GetProperty("mimi").GetProperty("inner_dim").GetInt32());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("model_id").ValueKind); // tanh GELU, pocket-tts's time norm
        Assert.Equal(["en"], root.GetProperty("languages").EnumerateArray().Select(l => l.GetString()));

        var written = File.GetLastWriteTimeUtc(path);
        PocketCheckpoint.WriteConfig(path);
        Assert.Equal(written, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public void Pocket_needs_its_library_and_an_avx2_cpu_on_x64()
    {
        Assert.Contains("missing", PocketEngine.WhyUnavailable(false, Architecture.X64, true));
        Assert.Contains("AVX2", PocketEngine.WhyUnavailable(true, Architecture.X64, false));
        Assert.Null(PocketEngine.WhyUnavailable(true, Architecture.X64, true));
        Assert.Null(PocketEngine.WhyUnavailable(true, Architecture.Arm64, false));
    }
}
