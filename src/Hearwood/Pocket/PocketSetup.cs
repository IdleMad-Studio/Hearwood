using System.Security.Cryptography;

namespace Hearwood.Pocket;

/// <summary>One pinned download: where it comes from, the file it becomes and its SHA-256.</summary>
public sealed record PocketDownload(string Url, string Local, string Sha256);

/// <summary>
/// Puts Pocket's checkpoint folder in place (ported from another project by the same author):
/// every file is downloaded from a pinned URL and checked by SHA-256, so a new upload can never change the audio.
/// </summary>
public sealed class PocketSetup(string checkpointDir)
{
    /// <summary>The checkpoint spoken with: 2026-10 reads sentence-final numbers that 2026-04 cuts off.</summary>
    public const string Checkpoint = "english_2026-10";

    /// <summary>Kyutai's repo without the gate; the same weights as the gated one (it only adds voice files ptts cannot read).</summary>
    public const string KyutaiLanguages =
        "https://huggingface.co/kyutai/pocket-tts-without-voice-cloning/resolve/a7056f505a4a8ca72d8b5fb1440071d6f1a35739/languages/";

    public const string KyutaiBase = KyutaiLanguages + Checkpoint + "/";

    /// <summary>Kyutai's weights as downloaded, before <see cref="PocketCheckpoint.SplitOffBos"/> writes the runtime's copy.</summary>
    public const string DownloadedWeightsFile = "kyutai-model.safetensors";

    public static readonly PocketDownload Tokenizer =
        new(KyutaiBase + "tokenizer.json", PocketEngine.TokenizerFile, "f498428e1eafee50492f7be13dc9bfafcfc12e508cd0eb1b01c92ecd5d8c6687");

    public static readonly PocketDownload Weights =
        new(KyutaiBase + "model.safetensors", DownloadedWeightsFile, "4c56cbd171c63e8a26ea61f5d48b8a0cd8f3c51f4652c0ab9a19bfcced8c931d");

    public string CheckpointDir => checkpointDir;

    public bool IsComplete => PocketEngine.FilesIn(checkpointDir).All(File.Exists);

    /// <summary>
    /// Makes the folder complete. <paramref name="onProgress"/> gets a status line and 0..1 progress of the
    /// download in hand; it is not called at all when every file is already there.
    /// </summary>
    public async Task EnsureAsync(Action<string, float>? onProgress = null, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(checkpointDir);
        PocketCheckpoint.WriteConfig(Path.Combine(checkpointDir, PocketEngine.ConfigFile));
        if (IsComplete)
            return;

        var tokenizer = Path.Combine(checkpointDir, Tokenizer.Local);
        if (!File.Exists(tokenizer))
            await DownloadVerifiedAsync(Tokenizer, tokenizer, onProgress, cancellationToken);

        var weights = Path.Combine(checkpointDir, PocketEngine.WeightsFile);
        var bos = Path.Combine(checkpointDir, PocketEngine.BosFile);
        if (!File.Exists(weights) || !File.Exists(bos))
        {
            var raw = Path.Combine(checkpointDir, DownloadedWeightsFile);
            if (File.Exists(raw) && !await HasHashAsync(raw, Weights.Sha256, cancellationToken))
                TryDelete(raw);
            if (!File.Exists(raw))
                await DownloadVerifiedAsync(Weights, raw, onProgress, cancellationToken);
            onProgress?.Invoke("Preparing the Pocket model…", 1f);
            await Task.Run(() => PocketCheckpoint.SplitOffBos(raw, weights, bos), cancellationToken);
            TryDelete(raw);
        }
    }

    /// <summary>A pinned download, kept only when its SHA-256 matches, so a corrupt file never stays in place.</summary>
    private static async Task DownloadVerifiedAsync(PocketDownload download, string target, Action<string, float>? onProgress, CancellationToken cancellationToken)
    {
        var label = $"Downloading {Path.GetFileName(download.Url)}";
        await DownloadFileAsync(download.Url, target, p => onProgress?.Invoke($"{label} ({p:P0})…", p), cancellationToken);
        if (!await HasHashAsync(target, download.Sha256, cancellationToken))
        {
            TryDelete(target);
            throw new InvalidDataException($"The Pocket download '{download.Local}' failed its integrity check. Try again.");
        }
    }

    internal static async Task<bool> HasHashAsync(string path, string sha256, CancellationToken cancellationToken)
    {
        byte[] hash;
        await using (var stream = File.OpenRead(path))
            hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return string.Equals(Convert.ToHexString(hash), sha256, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Downloads to a ".part" file and only then moves it into place. A short read against the
    /// announced Content-Length is rejected; callers check the pinned hash as well.
    /// </summary>
    private static async Task DownloadFileAsync(string url, string targetPath, Action<float>? onProgress, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        var partPath = targetPath + ".part";

        try
        {
            using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength;
            long written = 0;
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var target = new FileStream(partPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
            {
                var buffer = new byte[1 << 16];
                float lastReported = 0f;
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    written += read;
                    if (totalBytes is > 0 && onProgress is not null)
                    {
                        float progress = (float)((double)written / totalBytes.Value);
                        if (progress - lastReported >= 0.01f)
                        {
                            lastReported = progress;
                            onProgress(progress);
                        }
                    }
                }
            }

            if (totalBytes is long expected && written != expected)
                throw new IOException($"Download of '{Path.GetFileName(targetPath)}' was cut short ({written:N0} of {expected:N0} bytes). Try again.");
        }
        catch
        {
            TryDelete(partPath);
            throw;
        }

        File.Move(partPath, targetPath, overwrite: true);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
