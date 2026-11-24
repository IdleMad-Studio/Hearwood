using Hearwood.Text;

namespace Hearwood.Tests;

/// <summary>Numbers the model misreads as digits, written out instead; and "#" before a number.</summary>
public class NumberWordsTests
{
    [Theory]
    [InlineData(0, "zero")]
    [InlineData(15, "fifteen")]
    [InlineData(40, "forty")]
    [InlineData(74, "seventy-four")]
    [InlineData(100, "one hundred")]
    [InlineData(115, "one hundred fifteen")]
    [InlineData(174, "one hundred seventy-four")]
    [InlineData(1174, "one thousand one hundred seventy-four")]
    [InlineData(12500, "twelve thousand five hundred")]
    [InlineData(1_000_000, "one million")]
    [InlineData(2_003_004_005, "two billion three million four thousand five")]
    [InlineData(NumberWords.Max, "nine hundred ninety-nine billion nine hundred ninety-nine million nine hundred ninety-nine thousand nine hundred ninety-nine")]
    public void Whole_numbers_are_worded_as_counts(long n, string words) =>
        Assert.Equal(words, NumberWords.Cardinal(n));

    [Theory]
    [InlineData("I counted 174 boxes.", "I counted one hundred seventy-four boxes.")]
    [InlineData("Page 333, line 12.", "Page three hundred thirty-three, line 12.")]
    [InlineData("It sold 1,174 copies", "It sold one thousand one hundred seventy-four copies")]
    [InlineData("It sold 1174 copies", "It sold one thousand one hundred seventy-four copies")]
    [InlineData("(666)", "(six hundred sixty-six)")]
    [InlineData("174 percent", "one hundred seventy-four percent")]
    public void Whole_numbers_from_a_hundred_up_are_spelled(string text, string spoken) =>
        Assert.Equal(spoken, SpeechPlan.WithNumbersSpelled(text));

    [Theory]
    [InlineData("I have 42 cats.")]          // under a hundred
    [InlineData("It was 1999, then 2024.")]   // years
    [InlineData("It costs $174.")]            // money
    [InlineData("It costs 174 €.")]
    [InlineData("Pi is 3.14159.")]           // a decimal
    [InlineData("Up 174% today")]             // "%" ("174 percent" once WithPercentSpoken has run)
    [InlineData("Meet at 12:30.")]           // a time
    [InlineData("Pages 100-200.")]           // a range
    [InlineData("The 174th day.")]            // an ordinal
    [InlineData("Agent 007 and 0600.")]      // codes
    [InlineData("A 747 is not an MP3 or a 4K TV.")] // 747 is spelled; the rest is left
    [InlineData("It was -150 degrees.")]      // a minus
    [InlineData("Order 1234567890123 shipped")] // past a trillion
    public void Other_numbers_are_left_to_the_engine(string text)
    {
        var spoken = SpeechPlan.WithNumbersSpelled(text);
        if (text.Contains("747"))
            Assert.Equal("A seven hundred forty-seven is not an MP3 or a 4K TV.", spoken);
        else
            Assert.Equal(text, spoken);
    }

    [Theory]
    [InlineData("PR #174", "PR number 174")]
    [InlineData("#1 in the charts", "number 1 in the charts")]
    [InlineData("See issues #12 and #13.", "See issues number 12 and number 13.")]
    [InlineData("Play it in C#7.", "Play it in C#7.")]
    [InlineData("Use #hashtags and # signs.", "Use #hashtags and # signs.")]
    public void A_number_sign_before_a_number_is_said_as_number(string text, string spoken) =>
        Assert.Equal(spoken, SpeechPlan.WithNumberSignSpoken(text));

    [Fact]
    public void A_plan_reads_the_numbers_out_but_lights_the_words_as_written()
    {
        const string text = "Merged PR #174 today.";
        var piece = Assert.Single(SpeechPlan.Build(text));

        Assert.Equal("Merged PR number one hundred seventy-four today.", piece.Text);
        Assert.Equal(["Merged", "PR", "#174", "today."], piece.Words.Select(w => text.Substring(w.Start, w.Length)));
    }
}
