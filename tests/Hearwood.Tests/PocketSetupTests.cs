using Hearwood.Pocket;

namespace Hearwood.Tests;

public sealed class PocketSetupTests
{
    [Fact]
    public void Every_download_is_pinned_by_commit_and_hash()
    {
        foreach (var d in new[] { PocketSetup.Tokenizer, PocketSetup.Weights })
        {
            Assert.Matches("/resolve/[0-9a-f]{40}/", d.Url);
            Assert.Matches("^[0-9a-f]{64}$", d.Sha256);
        }
    }

    [Fact]
    public void A_bundled_model_counts_only_with_every_model_file_and_voice()
    {
        var dir = Path.Combine(Path.GetTempPath(), "hearwood-bundle-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "voices"));
            foreach (var f in PocketEngine.FilesIn(dir)) File.WriteAllText(f, "");
            Assert.False(PocketService.IsCompleteBundle(dir));

            foreach (var v in VoiceLibrary.BuiltIn.Keys) File.WriteAllText(PocketEngine.VoiceFile(dir, v), "");
            Assert.True(PocketService.IsCompleteBundle(dir));

            File.Delete(PocketEngine.VoiceFile(dir, PocketService.DefaultVoice));
            Assert.False(PocketService.IsCompleteBundle(dir));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void A_development_build_has_no_bundled_model() =>
        Assert.False(PocketService.IsCompleteBundle(PocketService.BundledCheckpointDir));
}
