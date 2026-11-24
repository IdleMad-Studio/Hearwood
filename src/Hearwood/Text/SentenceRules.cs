namespace Hearwood.Text;

/// <summary>
/// Where a sentence ends, as both the per-sentence speech of Pocket and subtitle cues
/// decide it, so a cue breaks where the voice paused. A sentence mark ends a sentence unless it
/// belongs to an abbreviation or the next word carries on in lower case: "3 p.m. today",
/// "\"No!\" he said", "Well... maybe". Anything else after the mark starts a new sentence, capitals
/// and digits as well as symbols ("$5 was left"), dashes and markdown.
/// </summary>
public static class SentenceRules
{
    private const string SentenceMarks = ".!?…";
    private static readonly char[] ClosingMarks = ['"', '\'', '”', '’', ')', ']'];
    private static readonly char[] OpeningMarks = ['"', '“', '‘', '\'', '(', '['];

    /// <summary>True when <paramref name="text"/> ends a sentence, given the word that follows it (null at the end).</summary>
    public static bool EndsSentence(string text, string? nextWord)
    {
        var bare = text.TrimEnd().TrimEnd(ClosingMarks);
        if (bare.Length == 0 || !SentenceMarks.Contains(bare[^1]))
            return false;
        if (bare[^1] == '.' && Abbreviations.EndsWithAbbreviation(bare))
            return false;
        return !ContinuesInLowerCase(nextWord);
    }

    /// <summary>True when the word starts in lower case, after any opening quote or bracket.</summary>
    public static bool ContinuesInLowerCase(string? word)
    {
        var start = (word ?? "").TrimStart().TrimStart(OpeningMarks);
        return start.Length > 0 && char.IsLower(start[0]);
    }
}
