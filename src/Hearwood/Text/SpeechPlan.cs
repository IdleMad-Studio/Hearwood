using System.Text.RegularExpressions;

namespace Hearwood.Text;

/// <summary>A word of the source text: where it is (UTF-16 offsets) and how long it takes to say, relative to its neighbours.</summary>
public readonly record struct WordSpan(int Start, int Length, int Weight);

/// <summary>One call's worth of speech: the text Pocket reads, the source words it covers and the silence after it.</summary>
public sealed record SpeechPiece(string Text, IReadOnlyList<WordSpan> Words, float PauseAfter);

/// <summary>
/// Cuts text into the pieces Pocket speaks, ported from another project by the same author: a piece per sentence (the model
/// runs sentences together inside one call, leaving nowhere to pause), long sentences balanced into pieces
/// of at most <see cref="MaxPieceLength"/>, each followed by a sentence, clause or paragraph pause. The model
/// reports no timestamps, so each word carries a weight from its length for estimating when it is heard.
/// </summary>
public static partial class SpeechPlan
{
    public const float SentencePause = 0.45f;
    public const float ClausePause = 0.1f;

    /// <summary>Pocket's quiet tail after the trim makes up the rest of a 1 s gap between paragraphs.</summary>
    public const float ParagraphPause = 0.75f;

    /// <summary>
    /// Longest text given to the library in one call; a run with no sentence ends (a contents page)
    /// would otherwise go in as one call of thousands of characters, with cancellation only per frame.
    /// </summary>
    public const int MaxPieceLength = 300;

    /// <summary>Saying a digit takes far longer than reading a letter: "22" is "twenty two".</summary>
    private const int DigitWeight = 3;

    /// <summary>Every word costs something beyond its letters, even "a".</summary>
    private const int WordWeight = 2;

    /// <summary>Speech stops briefly at a comma, so the next word starts later.</summary>
    private const int PunctuationWeight = 3;

    private static readonly char[] ClosingMarks = ['"', '\'', '”', '’', ')', ']'];
    private static readonly char[] Whitespace = [' ', '\t', '\r', '\n'];

    public static IReadOnlyList<SpeechPiece> Build(string text)
    {
        var pieces = new List<SpeechPiece>();
        int cursor = 0; // where the next word is looked for in the source
        var paragraphs = TextChunker.SplitParagraphs(text);
        for (int g = 0; g < paragraphs.Count; g++)
        {
            var sentences = SplitSentences(paragraphs[g]);
            for (int s = 0; s < sentences.Count; s++)
            {
                var parts = TextChunker.SplitBalanced(sentences[s], MaxPieceLength);
                var words = sentences[s].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                var shares = ShareWords(words, parts);
                for (int p = 0; p < parts.Count; p++)
                {
                    var spans = new List<WordSpan>(shares[p].Count);
                    foreach (var word in shares[p])
                    {
                        int at = text.IndexOf(word, cursor, StringComparison.Ordinal);
                        if (at < 0)
                            at = cursor; // cannot happen: every word is a verbatim slice of the text, in order
                        spans.Add(new WordSpan(at, word.Length, Weight(word)));
                        cursor = at + word.Length;
                    }

                    bool last = p == parts.Count - 1;
                    bool endsParagraph = last && s == sentences.Count - 1;
                    var spoken = WithPercentSpoken(parts[p]);
                    if (last)
                        spoken = WithSentenceStop(spoken);
                    pieces.Add(new SpeechPiece(spoken, spans, PauseAfter(spoken, endsParagraph && g < paragraphs.Count - 1)));
                }
            }
        }
        return pieces;
    }

    /// <summary>
    /// Sentences of a paragraph, by <see cref="SentenceRules"/>: "Mr. Harlow" and "3 p.m. today" stay
    /// whole. Pieces with nothing to say are dropped.
    /// </summary>
    public static IReadOnlyList<string> SplitSentences(string paragraph)
    {
        var sentences = new List<string>();
        int start = 0;
        foreach (Match gap in SentenceGap().Matches(paragraph))
        {
            int next = gap.Index + gap.Length;
            int nextEnd = paragraph.IndexOfAny(Whitespace, next);
            var candidate = paragraph[start..gap.Index];
            if (!SentenceRules.EndsSentence(candidate, paragraph[next..(nextEnd < 0 ? paragraph.Length : nextEnd)]))
                continue;
            sentences.Add(candidate);
            start = gap.Index + gap.Length;
        }
        sentences.Add(paragraph[start..]);
        return sentences.Select(s => s.Trim()).Where(s => s.Any(char.IsLetterOrDigit)).ToList();
    }

    /// <summary>Seconds of silence after a piece of text. Closing quotes do not hide the punctuation inside them.</summary>
    public static float PauseAfter(string text, bool endsParagraph)
    {
        float pause = TextChunker.PauseAfter(text.TrimEnd().TrimEnd(ClosingMarks), SentencePause, ClausePause);
        return endsParagraph ? Math.Max(pause, ParagraphPause) : pause;
    }

    /// <summary>
    /// The text with "." added when it ends in a letter or digit (a heading, list line or line of verse);
    /// without it the voice can trail on or stall.
    /// </summary>
    public static string WithSentenceStop(string text) =>
        text.Length > 0 && char.IsLetterOrDigit(text[^1]) ? text + "." : text;

    /// <summary>
    /// The text with a "%" after a number written as "percent": "9.99%" becomes "9.99 percent". ptts's English
    /// normaliser reads "9.99" as "9 point 99" only when nothing but sentence punctuation follows it, so it
    /// passes "9.99%" through untouched, and the model stops at the dot. A "%" before a letter or digit
    /// ("%20" in a URL) is left alone.
    /// </summary>
    public static string WithPercentSpoken(string text) =>
        text.Contains('%') ? PercentAfterNumber().Replace(text, " " + Percent) : text;

    /// <summary>Shares a sentence's words between the pieces it was split into, in proportion to the pieces' word counts.</summary>
    public static IReadOnlyList<IReadOnlyList<string>> ShareWords(IReadOnlyList<string> words, IReadOnlyList<string> pieces)
    {
        var counts = pieces.Select(p => p.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length).ToList();
        int total = Math.Max(counts.Sum(), 1);

        var shares = new List<IReadOnlyList<string>>();
        long cursor = 0;
        foreach (var count in counts)
        {
            int first = (int)(cursor * words.Count / total);
            cursor += count;
            int end = (int)(cursor * words.Count / total);
            shares.Add(words.Skip(first).Take(end - first).ToList());
        }
        return shares;
    }

    /// <summary>
    /// The word being heard <paramref name="elapsed"/> seconds into a piece of <paramref name="duration"/>
    /// seconds of speech, each word taking time by its weight; null when the piece has no words.
    /// </summary>
    public static WordSpan? WordAt(IReadOnlyList<WordSpan> words, double elapsed, double duration)
    {
        if (words.Count == 0)
            return null;
        double total = words.Sum(w => (double)w.Weight);
        double reached = duration <= 0 ? total : Math.Clamp(elapsed / duration, 0, 1) * total;
        double sum = 0;
        foreach (var word in words)
        {
            sum += word.Weight;
            if (reached < sum)
                return word;
        }
        return words[^1];
    }

    public static int Weight(string word)
    {
        int weight = WordWeight;
        foreach (char c in word)
        {
            if (char.IsDigit(c))
                weight += DigitWeight;
            else if (char.IsLetter(c))
                weight++;
            else if (c is ',' or ';' or ':' or '.' or '!' or '?' or '…' or '—')
                weight += PunctuationWeight;
            else if (c == '%') // spoken as a word of its own (WithPercentSpoken)
                weight += Percent.Length + (word.Length > 1 ? WordWeight : 0);
        }
        return weight;
    }

    private const string Percent = "percent";

    /// <summary>"%" after a digit, or one space after it, with no letter or digit following.</summary>
    [GeneratedRegex(@"(?<=\d)[   ]?%(?![\p{L}\p{N}])")]
    private static partial Regex PercentAfterNumber();

    /// <summary>White space after a sentence mark (and any closing marks): where a sentence may end. <see cref="SentenceRules"/> decides.</summary>
    [GeneratedRegex(@"(?<=[.!?…][""”’')\]]*)\s+(?=\S)")]
    private static partial Regex SentenceGap();
}
