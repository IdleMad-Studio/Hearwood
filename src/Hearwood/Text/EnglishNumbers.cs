using System.Text.RegularExpressions;

namespace Hearwood.Text;

/// <summary>
/// Numbers written out as English words before Pocket sees them. ptts leaves numbers under a thousand to the model as
/// digits, also inside larger ones ("1174" is "1 thousand 174"), and the model often drops or mangles their hundreds
/// ("174" came out as "seventy-four", "666" as "six six six"); written as words, they are read right.
/// <para>
/// Ported from another project by the same author (its <c>EnglishNumbers</c>, written for an engine with no normaliser
/// of its own). Each form has one rule and no rule looks at the words around a number, so the reading of a form never
/// changes with its sentence:
/// </para>
/// <list type="bullet">
/// <item>whole numbers, thousands commas, a minus, decimals ("point" and digits) and "%" ("nine point nine nine percent");</item>
/// <item>money after <c>$ £ € ¥</c>, with a scale ("$1.5M", "£3bn", "$5 million");</item>
/// <item>ordinals ("21st"); a number with "s": a decade for four digits ending in 0 or after an apostrophe ("1990s",
/// "'80s"), else the number's plural ("10s" "tens", "747s");</item>
/// <item>times: H:MM up to 23:59, and a dotted time with am/pm ("7.30pm"); am/pm attached or after one space;</item>
/// <item>a phone number with an area code, digit by digit ("555-123-4567", "(555) 123-4567", "555.123.4567"), also
/// after a leading "1" ("1-800-555-1234");</item>
/// <item>1100–1999 without a comma, minus or "%" always as a year ("eighteen fifty"), 2000–2099 always counted
/// ("two thousand fifteen");</item>
/// <item>a range, two numbers joined by "-" or "–" (one space either side of "–" too), each read on its own with "to"
/// between them ("1914-18" "nineteen fourteen to eighteen", "9:00-17:00", "$5-$10", "50%-60%"). This one is Hearwood's:
/// the original keeps the mark;</item>
/// <item>numbers otherwise joined by "-", "/" or ":" each on their own, the mark kept ("2024-05-01", "24/7", "16:9").</item>
/// </list>
/// A number glued to letters is left exactly as written ("MP3", "4K", "2.5kg", "12:30ish"); a number after a hyphen
/// ("COVID-19") is read. A "-" is a minus only at the start of a word ("it was -5"), so a dash after a number's
/// bracket ("(3)-4") is kept as a mark.
/// </summary>
public static partial class EnglishNumbers
{
    static readonly string[] Ones =
    [
        "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten",
        "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen",
    ];

    static readonly string[] Tens = ["", "", "twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety"];

    static readonly (long Value, string Name)[] Scales =
        [(1_000_000_000_000, "trillion"), (1_000_000_000, "billion"), (1_000_000, "million"), (1_000, "thousand")];

    /// <summary>Longest run of digits read as one number; a longer one (an ID) is read digit by digit.</summary>
    const int MaxNumberDigits = 15;

    /// <summary>"am"/"pm" after a number, attached or after one space: <c>am pm AM PM a.m. p.m. A.M. P.M.</c></summary>
    const string MeridiemSuffix = @"[  ]?(?:[ap]\.m\.?|[AP]\.M\.?|[ap]m|[AP]M)(?![\p{L}])";

    /// <summary>
    /// Where a "-" can be a minus: at the start of the text, after a space or after an opening bracket or quote. After
    /// anything else ("(3)-4") it is a dash and is kept.
    /// </summary>
    const string MinusStart = @"(?<=^|[\s(\[{""“‘'«])";

    /// <summary>A "%" as written after a number: straight after it, or after one space (plain, no-break or narrow no-break).</summary>
    const string PercentSign = @"[   ]?%";

    /// <summary>
    /// One end of a range: a time, or a number with an optional currency sign before it and a decimal part and "%" or
    /// "°" after it.
    /// </summary>
    const string RangeEnd = @"(?:[0-9]{1,2}:[0-5][0-9](?![0-9])|[$£€¥]?(?:[0-9]{1,3}(?:,[0-9]{3})+(?![0-9])|[0-9]+)(?:\.[0-9]+)?(?:" + PercentSign + "|°)?)";

    /// <summary>The text with every number it can read written out; text without digits comes back unchanged.</summary>
    public static string Spell(string text) =>
        text.Any(char.IsAsciiDigit) ? Number().Replace(text, m => Speak(m, text)) : text;

    static string Speak(Match m, string text)
    {
        var g = m.Groups;
        int end = m.Index + m.Length;
        if (g["from"].Success)
            return Spell(g["from"].Value) + " to " + Spell(g["to"].Value);
        if (g["sym"].Success)
            // After letters ("US$5") the reading starts a word of its own: "US five dollars".
            return (m.Index > 0 && char.IsLetter(text[m.Index - 1]) ? " " : "") + (g["mneg"].Success ? "minus " : "")
                + Money(g["sym"].Value[0], g["cint"].Value, g["cfrac"].Value, ScaleOf(g["scale"].Value, g["abbr"].Value));
        if (g["phone"].Success)
            return string.Join(", ", DigitGroups().Matches(g["phone"].Value).Select(d => Digits(d.Value)));
        if (g["chain"].Success)
            return string.Join(" point ", g["chain"].Value.Split('.').Select(Integer));
        if (g["h"].Success)
        {
            int hour = int.Parse(g["h"].Value);
            return hour <= 23
                ? Time(hour, int.Parse(g["m"].Value), g["tampm"].Value.Trim(), text, end)
                : Joined(m.Value);
        }
        if (g["bh"].Success)
            return Time(int.Parse(g["bh"].Value), int.Parse(g["bm"].Value), g["bampm"].Value.Trim(), text, end);
        if (g["joined"].Success)
            return Joined(g["joined"].Value);
        if (g["ord"].Success)
            return Ordinal(Integer(g["ord"].Value));
        if (g["dec"].Success)
            return Plurals(g["dec"].Value, g["apos"].Success);

        var spoken = new System.Text.StringBuilder();
        if (g["neg"].Success)
            spoken.Append("minus ");
        if (g["int"].Success)
            // Only a number with no minus, decimal or "%" can be a year: "1500%" is counted.
            spoken.Append(g["frac"].Success || g["neg"].Success || g["pct"].Success
                ? Integer(g["int"].Value)
                : Whole(g["int"].Value));
        if (g["frac"].Success)
            spoken.Append(g["int"].Success ? " point " : "point ").Append(Digits(g["frac"].Value));
        if (g["pct"].Success)
            spoken.Append(" percent");
        if (g["ampm"].Success)
            spoken.Append(' ').Append(Meridiem(g["ampm"].Value.Trim(), text, end));
        return spoken.ToString();
    }

    /// <summary>A whole number without a minus or "%": 1100 to 1999 without a comma as a year ("eighteen fifty"), else counted.</summary>
    static string Whole(string digits) =>
        digits.Length == 4 && int.TryParse(digits, out int year) && year is >= 1100 and <= 1999 ? Year(year) : Integer(digits);

    /// <summary>Numbers joined by "-", "/" or ":" ("2024-05-01", "24/7", "16:9"), each read on its own with its mark kept.</summary>
    static string Joined(string written) => DigitGroups().Replace(written, d => Whole(d.Value));

    /// <summary>
    /// A number with "s": a decade for four digits ending in 0 ("1990s", "2010s") or after an apostrophe ("'80s",
    /// "'00s" "two thousands"); otherwise the number's plural ("10s" "tens", "the 500s" "five hundreds", "747s").
    /// </summary>
    static string Plurals(string digits, bool apostrophe)
    {
        bool grouped = digits.Contains(',');
        digits = digits.Replace(",", "");
        if (apostrophe && digits.Length == 2)
            return digits == "00" ? "two thousands" : Plural(UnderHundred(int.Parse(digits)));
        if (!grouped && digits.Length == 4 && digits.EndsWith('0') && int.Parse(digits) is >= 1100 and <= 2099)
            return Plural(Year(int.Parse(digits)));
        return Plural(Integer(digits));
    }

    /// <summary>Digits (with optional thousands commas) as one number; leading zeros or too many digits are read one by one.</summary>
    static string Integer(string digits)
    {
        digits = digits.Replace(",", "");
        if (digits.Length == 0)
            return "zero";
        if (digits.Length > MaxNumberDigits || (digits.Length > 1 && digits[0] == '0'))
            return Digits(digits);
        return Cardinal(long.Parse(digits));
    }

    static string Digits(string digits) => string.Join(' ', digits.Where(char.IsAsciiDigit).Select(c => Ones[c - '0']));

    /// <summary>A whole number counted: "one hundred seventy-four".</summary>
    public static string Cardinal(long n)
    {
        if (n < 1000)
            return UnderThousand((int)n);
        var parts = new List<string>();
        foreach (var (value, name) in Scales)
        {
            if (n >= value)
            {
                parts.Add(Cardinal(n / value) + " " + name);
                n %= value;
            }
        }
        if (n > 0)
            parts.Add(UnderThousand((int)n));
        return string.Join(' ', parts);
    }

    static string UnderHundred(int n) =>
        n < 20 ? Ones[n] : Tens[n / 10] + (n % 10 > 0 ? "-" + Ones[n % 10] : "");

    static string UnderThousand(int n) =>
        n >= 100 ? Ones[n / 100] + " hundred" + (n % 100 > 0 ? " " + UnderHundred(n % 100) : "") : UnderHundred(n);

    /// <summary>"1999" as "nineteen ninety-nine", "1905" "nineteen oh five", "1900" "nineteen hundred"; round thousands and 2000-2009 as counted.</summary>
    static string Year(int year)
    {
        if (year % 1000 == 0 || year is >= 2000 and <= 2009)
            return Cardinal(year);
        int high = year / 100, low = year % 100;
        return UnderHundred(high) + (low == 0 ? " hundred" : low < 10 ? " oh " + Ones[low] : " " + UnderHundred(low));
    }

    /// <summary>
    /// "3:30" as "three thirty", "3:05" "three oh five", "3:00" "three o'clock" (just "three" before a.m. or p.m.);
    /// a 24-hour time on the hour as "hundred" ("18:00" "eighteen hundred").
    /// </summary>
    static string Time(int hour, int minute, string meridiem, string text, int end)
    {
        var spoken = Cardinal(hour) + (minute == 0
            ? (meridiem.Length > 0 ? "" : hour is 0 or > 12 ? " hundred" : " o'clock")
            : minute < 10 ? " oh " + Ones[minute] : " " + Cardinal(minute));
        return meridiem.Length > 0 ? spoken + " " + Meridiem(meridiem, text, end) : spoken;
    }

    /// <summary>"pm" or "p.m." after a number, as "p.m."; the final dot is left to the text's own full stop when one follows.</summary>
    static string Meridiem(string written, string text, int end) =>
        char.ToLowerInvariant(written[0]) + (end < text.Length && text[end] == '.' ? ".m" : ".m.");

    static string Ordinal(string cardinal)
    {
        int cut = cardinal.LastIndexOfAny([' ', '-']) + 1;
        var last = cardinal[cut..];
        var ordinal = last switch
        {
            "one" => "first",
            "two" => "second",
            "three" => "third",
            "five" => "fifth",
            "eight" => "eighth",
            "nine" => "ninth",
            "twelve" => "twelfth",
            _ when last.EndsWith('y') => last[..^1] + "ieth",
            _ => last + "th",
        };
        return cardinal[..cut] + ordinal;
    }

    /// <summary>"nineteen ninety" as "nineteen nineties", "six" "sixes"; "one hundred" as "hundreds", as in "the 100s".</summary>
    static string Plural(string words)
    {
        if (words.StartsWith("one ", StringComparison.Ordinal) && !words[4..].Contains(' '))
            words = words[4..];
        return words.EndsWith('y') ? words[..^1] + "ies" : words.EndsWith('x') ? words + "es" : words + "s";
    }

    static string ScaleOf(string word, string letter) =>
        word.Length > 0 ? word.ToLowerInvariant() : letter switch
        {
            "" => "",
            "k" or "K" => "thousand",
            "m" or "M" => "million",
            "bn" or "B" => "billion",
            _ => "trillion",
        };

    static string Money(char symbol, string whole, string fraction, string scale)
    {
        var (unit, units, cent, cents) = symbol switch
        {
            '$' => ("dollar", "dollars", "cent", "cents"),
            '£' => ("pound", "pounds", "penny", "pence"),
            '€' => ("euro", "euros", "cent", "cents"),
            _ => ("yen", "yen", "", ""),
        };

        var amount = Integer(whole);
        if (scale.Length > 0)
            return amount + (fraction.Length > 0 ? " point " + Digits(fraction) : "") + " " + scale + " " + units;
        if (fraction.Length > 0 && (fraction.Length != 2 || cent.Length == 0))
            return amount + " point " + Digits(fraction) + " " + units;

        var number = whole.Replace(",", "");
        var main = amount + " " + (number == "1" ? unit : units);
        if (fraction.Length == 0)
            return main;
        int hundredths = int.Parse(fraction);
        if (hundredths == 0)
            return main;
        var change = Cardinal(hundredths) + " " + (hundredths == 1 ? cent : cents);
        return number.TrimStart('0').Length == 0 ? change : main + " and " + change;
    }

    // One pass, longest forms first. A range comes first, so its ends are not taken as money, times or numbers on
    // their own; it is two numbers only, not the middle or start of a chain ("2024-05-01", "555-123-4567"). Money may
    // follow letters ("US$5" is "US five dollars"); every other form starts after no letter or digit and is never the
    // "000" of a thousands number ("1,000" is not "1," and "000"). A minus starts a word (MinusStart). The final guard
    // refuses a match glued to more letters or digits, or stopping at a ".", ":" or "%" glued to them, so "2.5kg"
    // stays whole instead of becoming "two.5kg", or stopping before the ",000" of a thousands number; four digits
    // before a comma cannot start one, so "1990,500" is two numbers.
    [GeneratedRegex("""
        (?:
            (?<![\p{L}\p{N}_]|[\p{L}\p{N}][.:]|[0-9][-–])
            (?<from>
        """ + RangeEnd + """
        )(?:-|[  ]?–[  ]?)(?<to>
        """ + RangeEnd + """
        )(?![-–/:]?[0-9])
          | (?:
        """ + MinusStart + """
        (?<mneg>[-−]))?(?<![\p{N}_])(?<sym>[$£€¥])(?=\.?[0-9])
                (?<cint>[0-9]{1,3}(?:,[0-9]{3})+(?![0-9])|[0-9]+)?(?:\.(?<cfrac>[0-9]+))?
                (?:[  ](?<scale>(?i:thousand|million|billion|trillion))(?![\p{L}])|(?<abbr>bn|tn|[kKmMB])(?![\p{L}]))?
          | (?<![\p{L}\p{N}_]|[\p{L}\p{N}][.:])
            (?!(?<=(?<![0-9])[0-9]{1,3},)[0-9]{3}(?![0-9]))
            (?:
                (?<phone>(?:1[  ])?\([0-9]{3}\)[  ]?[0-9]{3}-[0-9]{4}|(?:1-)?[0-9]{3}-[0-9]{3}-[0-9]{4}|(?:1\.)?[0-9]{3}\.[0-9]{3}\.[0-9]{4})
              | (?<chain>[0-9]+(?:\.[0-9]+){2,})
              | (?<h>[0-9]{1,2}):(?<m>[0-5][0-9])(?![0-9])(?<tampm>
        """ + MeridiemSuffix + """
        )?
              | (?<bh>[01]?[0-9]|2[0-3])\.(?<bm>[0-5][0-9])(?<bampm>
        """ + MeridiemSuffix + """
        )
              | (?<joined>[0-9]+(?:[-–/:][0-9]+)+)
              | (?<ord>[0-9]{1,3}(?:,[0-9]{3})+|[0-9]+)(?i:st|nd|rd|th)
              | (?<apos>['’])?(?<dec>[0-9]{1,3}(?:,[0-9]{3})+|[0-9]+)s
              | (?=[-−]?\.?[0-9])
                (?:
        """ + MinusStart + """
        (?<neg>[-−])(?=\.?[0-9]))?
                (?<int>[0-9]{1,3}(?:,[0-9]{3})+(?![0-9])|[0-9]+)?
                (?:\.(?<frac>[0-9]+))?
                (?:(?<pct>
        """ + PercentSign + """
        )|(?<ampm>
        """ + MeridiemSuffix + """
        ))?
            )
        )
        (?![\p{L}\p{N}]|[.:%][\p{L}\p{N}]|(?<![0-9]{4}),[0-9]{3}(?![0-9]))
        """, RegexOptions.IgnorePatternWhitespace)]
    private static partial Regex Number();

    [GeneratedRegex("[0-9]+")]
    private static partial Regex DigitGroups();
}
