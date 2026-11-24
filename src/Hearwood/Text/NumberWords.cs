namespace Hearwood.Text;

/// <summary>
/// Whole numbers as English words, "one hundred seventy-four", worded as in another project by the same author
/// (its <c>EnglishNumbers.Cardinal</c>), so the two apps read numbers alike.
/// </summary>
public static class NumberWords
{
    static readonly string[] Ones =
    [
        "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten",
        "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen",
    ];

    static readonly string[] Tens = ["", "", "twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety"];

    static readonly (long Value, string Name)[] Scales = [(1_000_000_000, "billion"), (1_000_000, "million"), (1_000, "thousand")];

    /// <summary>The largest number <see cref="Cardinal"/> words: under a trillion.</summary>
    public const long Max = 999_999_999_999;

    public static string Cardinal(long n)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(n);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(n, Max);
        if (n < 1000)
            return UnderThousand((int)n);
        var parts = new List<string>();
        foreach (var (value, name) in Scales)
        {
            if (n >= value)
            {
                parts.Add(UnderThousand((int)(n / value)) + " " + name);
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
}
