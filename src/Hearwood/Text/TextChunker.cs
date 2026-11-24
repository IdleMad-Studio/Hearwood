using System.Text.RegularExpressions;

namespace Hearwood.Text;

/// <summary>
/// Splits text into pieces short enough for one synthesis call, cutting at the least
/// disruptive boundary available: sentence mark, clause mark, then word gap.
/// Ported from another project by the same author, which ported it from kokoro-onnx's <c>chunker.py</c>.
/// </summary>
public static partial class TextChunker
{
    private const string SentenceMarks = ".!?\u2026";
    private const string ClauseMarks = ",;:";

    // Least to most disruptive place to cut; punctuation stays with the text
    // before it, which is how the model was trained.
    [GeneratedRegex("(?<=[.!?\\u2026])\\s+")]
    private static partial Regex SentenceBoundary();

    [GeneratedRegex("(?<=[,;:])\\s+")]
    private static partial Regex ClauseBoundary();

    [GeneratedRegex("\\s+")]
    private static partial Regex Whitespace();

    private static IEnumerable<string> Atoms(string phonemes, int maxLength, int level = 0)
    {
        if (phonemes.Length <= maxLength)
        {
            if (phonemes.Length > 0)
                yield return phonemes;
            yield break;
        }

        for (int level2 = level; level2 < 3; level2++)
        {
            var pieces = (level2 switch
            {
                0 => SentenceBoundary().Split(phonemes),
                1 => ClauseBoundary().Split(phonemes),
                _ => Whitespace().Split(phonemes),
            });

            if (pieces.Length > 1)
            {
                foreach (var piece in pieces)
                    foreach (var atom in Atoms(piece.Trim(), maxLength, level2 + 1))
                        yield return atom;
                yield break;
            }
        }

        // A single unbroken run longer than the context: slice it rather than
        // dropping the tail.
        for (int start = 0; start < phonemes.Length; start += maxLength)
            yield return phonemes[start..Math.Min(start + maxLength, phonemes.Length)];
    }

    /// <summary>Groups consecutive atoms into batches within <paramref name="limit"/>, as index ranges.</summary>
    private static List<(int Start, int End)> Pack(List<int> lengths, int limit)
    {
        var batches = new List<(int, int)>();
        (int Start, int End) current = (0, 0);
        int size = 0;

        for (int index = 0; index < lengths.Count; index++)
        {
            int candidate = index == current.Start ? lengths[index] : size + 1 + lengths[index];
            if (candidate > limit && index > current.Start)
            {
                batches.Add((current.Start, index));
                current = (index, index + 1);
                size = lengths[index];
            }
            else
            {
                size = candidate;
                current = (current.Start, index + 1);
            }
        }

        if (lengths.Count > 0)
            batches.Add((current.Start, lengths.Count));

        return batches;
    }

    /// <summary>
    /// Splits at sentence ends, then clauses, then spaces, into evenly-sized pieces of at most
    /// <paramref name="maxLength"/>. Filling each piece to the limit would leave a short remainder,
    /// which is spoken at a different rate and loudness than its neighbors, so pieces are
    /// balanced: the smallest limit that still needs no extra piece.
    /// </summary>
    public static IReadOnlyList<string> SplitBalanced(string text, int maxLength)
    {
        text = text.Trim();
        if (text.Length == 0)
            return [];

        var atoms = Atoms(text, maxLength).ToList();
        if (atoms.Count == 0)
            return [];

        var lengths = atoms.Select(a => a.Length).ToList();
        int fewest = Pack(lengths, maxLength).Count;

        int low = lengths.Max(), high = maxLength;
        while (low < high)
        {
            int middle = (low + high) / 2;
            if (Pack(lengths, middle).Count <= fewest)
                high = middle;
            else
                low = middle + 1;
        }

        return Pack(lengths, low)
            .Select(range => string.Join(' ', atoms.Skip(range.Start).Take(range.End - range.Start)))
            .ToList();
    }

    /// <summary>Splits source text into its non-blank lines (each line/paragraph is synthesized separately).</summary>
    public static IReadOnlyList<string> SplitParagraphs(string text) =>
        text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Quotes and brackets that close after the punctuation: <c>"I'm leaving."</c> ends a sentence.</summary>
    private static readonly char[] ClosingMarks = ['"', '\'', '”', '’', '»', ')', ']'];

    /// <summary>Seconds of silence a batch ending with this text (closing quotes and brackets aside) should be followed by.</summary>
    public static float PauseAfter(string batch, float sentencePause, float clausePause)
    {
        string trimmed = batch.TrimEnd().TrimEnd(ClosingMarks).TrimEnd();
        if (trimmed.Length == 0)
            return 0f;

        char mark = trimmed[^1];
        if (SentenceMarks.Contains(mark))
            return sentencePause;
        if (ClauseMarks.Contains(mark))
            return clausePause;
        return 0f;
    }
}
