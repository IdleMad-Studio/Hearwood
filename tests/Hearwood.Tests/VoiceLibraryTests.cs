using Hearwood.Pocket;

namespace Hearwood.Tests;

public sealed class VoiceLibraryTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "hearwood-voices-" + Guid.NewGuid().ToString("N"));
    readonly string _checkpoint;
    readonly VoiceLibrary _library;

    public VoiceLibraryTests()
    {
        Directory.CreateDirectory(_dir);
        _checkpoint = Path.Combine(_dir, "checkpoint");
        _library = new VoiceLibrary(_checkpoint);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Every_built_in_voice_ships_a_voice_file_and_the_default_is_one_of_them()
    {
        Assert.True(_library.Exists(PocketService.DefaultVoice));
        Assert.Equal(15, VoiceLibrary.BuiltIn.Count);
        Assert.Equal(VoiceLibrary.BuiltIn.Keys, _library.List());
        Assert.All(VoiceLibrary.BuiltIn.Keys, name =>
            Assert.True(File.Exists(Path.Combine(VoiceLibrary.BundledFolder, name + ".safetensors")), $"No bundled voice file for {name}."));
        Assert.Equal(VoiceLibrary.BuiltIn.Count, Directory.GetFiles(VoiceLibrary.BundledFolder).Length);
    }

    [Fact]
    public void Each_bundled_voice_file_is_a_ptts_voice_of_whole_1024_wide_frames()
    {
        Assert.All(VoiceLibrary.BuiltIn.Keys, name =>
        {
            var info = PocketCheckpoint.ReadTensorInfo(Path.Combine(VoiceLibrary.BundledFolder, name + ".safetensors"));
            var (dtype, shape) = Assert.Single(info, t => t.Key == PocketCheckpoint.VoiceTensor).Value;
            Assert.Equal("F32", dtype);
            Assert.Equal(3, shape.Length);
            Assert.Equal(1, shape[0]);
            Assert.True(shape[1] > 1, $"{name} has no frames after the BOS frame.");
            Assert.Equal(1024, shape[2]);
        });
    }

    [SkippableFact]
    public void Each_bundled_voice_opens_with_the_checkpoints_bos_frame()
    {
        var bosPath = Path.Combine(PocketService.DataDir, "kyutai-" + PocketSetup.Checkpoint, PocketEngine.BosFile);
        Skip.IfNot(File.Exists(bosPath), "The Pocket model is not in place yet; run Hearwood once.");
        var bos = NumpyIO.Read(bosPath).Data;
        Assert.All(VoiceLibrary.BuiltIn.Keys, name =>
        {
            var frames = PocketCheckpoint.ReadTensor(Path.Combine(VoiceLibrary.BundledFolder, name + ".safetensors"), PocketCheckpoint.VoiceTensor);
            Assert.Equal(bos, frames[..bos.Length]);
        });
    }

    [Fact]
    public void Installing_copies_every_voice_and_replaces_a_copy_that_differs()
    {
        var bundled = Path.Combine(_dir, "bundled");
        Directory.CreateDirectory(bundled);
        foreach (var name in VoiceLibrary.BuiltIn.Keys)
            File.WriteAllText(Path.Combine(bundled, name + ".safetensors"), "voice " + name);
        var willow = PocketEngine.VoiceFile(_checkpoint, "Willow");
        Directory.CreateDirectory(Path.GetDirectoryName(willow)!);
        File.WriteAllText(willow, "an older Willow");

        _library.InstallBundled(bundled);

        Assert.All(VoiceLibrary.BuiltIn.Keys, name =>
            Assert.Equal("voice " + name, File.ReadAllText(PocketEngine.VoiceFile(_checkpoint, name))));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(willow)!, "*.part"));
    }

    [Fact]
    public void A_missing_bundled_voice_file_is_reported()
    {
        var bundled = Path.Combine(_dir, "bundled");
        Directory.CreateDirectory(bundled);
        var ex = Assert.Throws<FileNotFoundException>(() => _library.InstallBundled(bundled));
        Assert.Contains("Willow", ex.Message);
    }

    [Fact]
    public void Voices_are_labelled_with_how_they_sound()
    {
        Assert.Equal("Willow – American, female", VoiceLibrary.Label("Willow"));
        Assert.Equal("Birch – British, male", VoiceLibrary.Label("Birch"));
        Assert.Equal("Unknown", VoiceLibrary.Label("Unknown"));
        Assert.False(_library.Exists("Alex"));
    }
}
