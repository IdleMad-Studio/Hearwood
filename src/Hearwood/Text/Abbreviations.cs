using System.Text.RegularExpressions;

namespace Hearwood.Text;

/// <summary>Words whose period does not end a sentence, because a name or more of the sentence follows.</summary>
public static partial class Abbreviations
{
    /// <summary>
    /// True for initials ("J." or "U.S.S."), titles ("Mr.", "DR.", "MR.") and "vs.", "e.g.", "i.e.",
    /// with any opening quote or bracket. Titles match in any case, initials only as capitals. Only the
    /// last word of the text counts.
    /// </summary>
    public static bool EndsWithAbbreviation(string text)
    {
        var trimmed = text.TrimEnd();
        return Pattern().IsMatch(trimmed[(trimmed.LastIndexOfAny([' ', '\t', '\r', '\n']) + 1)..]);
    }

    [GeneratedRegex(@"^[""“‘'(\[]*(?:(?:\p{Lu}\.)+|(?i:mr|mrs|ms|dr|prof|st|sr|jr|mt|rev|gen|col|capt|lt|sgt|vs|e\.g|i\.e)\.)$")]
    private static partial Regex Pattern();
}
