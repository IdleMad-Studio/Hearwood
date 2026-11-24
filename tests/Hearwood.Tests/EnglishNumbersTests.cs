using Hearwood.Text;

namespace Hearwood.Tests;

/// <summary>
/// One theory per form of the number table, ported from another project by the same author with its tests, and one
/// pinning the known limits: a reading there is a decision, not a bug. Ranges are Hearwood's own: they say "to".
/// </summary>
public class EnglishNumbersTests
{
    [Theory]
    [InlineData("22 men came.", "twenty-two men came.")]
    [InlineData("He had 1,234,567 coins.", "He had one million two hundred thirty-four thousand five hundred sixty-seven coins.")]
    [InlineData("It was -5 outside.", "It was minus five outside.")]
    [InlineData("Count 1, 2, 3.", "Count one, two, three.")]
    [InlineData("Agent 007 and 1234567890123456.", "Agent zero zero seven and one two three four five six seven eight nine zero one two three four five six.")]
    [InlineData("A 1500% rise to -1999.", "A one thousand five hundred percent rise to minus one thousand nine hundred ninety-nine.")]
    [InlineData("In 1990,500 people.", "In nineteen ninety,five hundred people.")]
    [InlineData("(-5) and \"-5\" and (3)-4.", "(minus five) and \"minus five\" and (three)-four.")]
    [InlineData("I counted 174 boxes.", "I counted one hundred seventy-four boxes.")]
    [InlineData("It sold 1,174 copies.", "It sold one thousand one hundred seventy-four copies.")]
    public void Whole_numbers_are_counted(string text, string spoken) =>
        Assert.Equal(spoken, EnglishNumbers.Spell(text));

    [Theory]
    [InlineData("Prices rose by 9.99% last year.", "Prices rose by nine point nine nine percent last year.")]
    [InlineData("Up 12 % today.", "Up twelve percent today.")]
    [InlineData("It cost 0.5 and .25 more.", "It cost zero point five and point two five more.")]
    [InlineData("Version 1.2.3 is out.", "Version one point two point three is out.")]
    [InlineData("Pi is 3.174.", "Pi is three point one seven four.")]
    public void Decimals_and_percent_are_read_with_point(string text, string spoken) =>
        Assert.Equal(spoken, EnglishNumbers.Spell(text));

    [Theory]
    [InlineData("It cost $5.", "It cost five dollars.")]
    [InlineData("It cost $1.", "It cost one dollar.")]
    [InlineData("It cost $5.99 each.", "It cost five dollars and ninety-nine cents each.")]
    [InlineData("It cost $.50 then.", "It cost fifty cents then.")]
    [InlineData("It cost $174.", "It cost one hundred seventy-four dollars.")]
    [InlineData("It was £2.01 or €3 or ¥500.", "It was two pounds and one penny or three euros or five hundred yen.")]
    [InlineData("A $1.5M deal and £3bn.", "A one point five million dollars deal and three billion pounds.")]
    [InlineData("A $5 million loss.", "A five million dollars loss.")]
    [InlineData("Only $5k down, or -$5.", "Only five thousand dollars down, or minus five dollars.")]
    public void Money_is_read_with_its_unit_and_scale(string text, string spoken) =>
        Assert.Equal(spoken, EnglishNumbers.Spell(text));

    [Theory]
    [InlineData("The 21st, 2nd, 3rd, 12th and 100th.", "The twenty-first, second, third, twelfth and one hundredth.")]
    [InlineData("The 1,000th time.", "The one thousandth time.")]
    public void Ordinals_are_read_as_ordinals(string text, string spoken) =>
        Assert.Equal(spoken, EnglishNumbers.Spell(text));

    [Theory]
    [InlineData("The 1990s, the 2010s and the 1800s.", "The nineteen nineties, the twenty tens and the eighteen hundreds.")]
    [InlineData("The '80s and the '00s.", "The eighties and the two thousands.")]
    [InlineData("Two 6s and the 500s.", "Two sixes and the five hundreds.")]
    [InlineData("Boeing 747s and F-16s.", "Boeing seven hundred forty-sevens and F-sixteens.")]
    [InlineData("By the 1,000s.", "By the thousands.")]
    public void A_number_with_s_is_a_decade_or_the_numbers_plural(string text, string spoken) =>
        Assert.Equal(spoken, EnglishNumbers.Spell(text));

    [Theory]
    [InlineData("At 3:30 we left.", "At three thirty we left.")]
    [InlineData("At 3:05 we left.", "At three oh five we left.")]
    [InlineData("At 10:00 we left.", "At ten o'clock we left.")]
    [InlineData("At 18:00 we left.", "At eighteen hundred we left.")]
    [InlineData("At 10:00 p.m. we left.", "At ten p.m. we left.")]
    [InlineData("We left at 5pm.", "We left at five p.m.")]
    [InlineData("At 10 AM and 7.30pm.", "At ten a.m. and seven thirty p.m.")]
    [InlineData("At 12:30:45 sharp.", "At twelve:thirty:forty-five sharp.")]
    public void Times_are_read_as_times(string text, string spoken) =>
        Assert.Equal(spoken, EnglishNumbers.Spell(text));

    [Theory]
    [InlineData("Call 555-123-4567.", "Call five five five, one two three, four five six seven.")]
    [InlineData("Call (555) 123-4567.", "Call five five five, one two three, four five six seven.")]
    [InlineData("Call 555.123.4567.", "Call five five five, one two three, four five six seven.")]
    [InlineData("Call 1-800-555-1234 now.", "Call one, eight zero zero, five five five, one two three four now.")]
    [InlineData("Call 1 (800) 555-1234.", "Call one, eight zero zero, five five five, one two three four.")]
    public void A_phone_number_with_an_area_code_is_read_digit_by_digit(string text, string spoken) =>
        Assert.Equal(spoken, EnglishNumbers.Spell(text));

    [Theory]
    [InlineData("In 1999 it ended.", "In nineteen ninety-nine it ended.")]
    [InlineData("By 1900 and 1905.", "By nineteen hundred and nineteen oh five.")]
    [InlineData("In 2005 and 2026.", "In two thousand five and two thousand twenty-six.")]
    [InlineData("It sold 1,850 copies.", "It sold one thousand eight hundred fifty copies.")]
    [InlineData("A crew of 1500 men.", "A crew of fifteen hundred men.")]
    public void Years_1100_to_1999_are_years_and_2000_on_are_counted(string text, string spoken) =>
        Assert.Equal(spoken, EnglishNumbers.Spell(text));

    [Theory]
    [InlineData("From 1914-1918.", "From nineteen fourteen to nineteen eighteen.")]
    [InlineData("From 1914-18.", "From nineteen fourteen to eighteen.")]
    [InlineData("Pages 10-20.", "Pages ten to twenty.")]
    [InlineData("Pages 100-200 and 3–5.", "Pages one hundred to two hundred and three to five.")]
    [InlineData("Ages 10 – 12.", "Ages ten to twelve.")]
    [InlineData("A 50%-60% chance.", "A fifty percent to sixty percent chance.")]
    [InlineData("From 5°-10° and 5-10°C.", "From five° to ten° and five to ten°C.")]
    [InlineData("It costs $5-$10.", "It costs five dollars to ten dollars.")]
    [InlineData("From 1.5-2.5 litres.", "From one point five to two point five litres.")]
    [InlineData("Open 9:00-17:00 and 10:30–11:30.", "Open nine o'clock to seventeen hundred and ten thirty to eleven thirty.")]
    public void A_range_of_two_numbers_is_read_with_to(string text, string spoken) =>
        Assert.Equal(spoken, EnglishNumbers.Spell(text));

    [Theory]
    [InlineData("Open 24/7.", "Open twenty-four/seven.")]
    [InlineData("A 16:9 screen.", "A sixteen:nine screen.")]
    [InlineData("On 2024-05-01.", "On two thousand twenty-four-zero five-zero one.")]
    public void Numbers_otherwise_joined_by_a_mark_are_each_read_with_the_mark_kept(string text, string spoken) =>
        Assert.Equal(spoken, EnglishNumbers.Spell(text));

    [Theory]
    [InlineData("An MP3 in 4K, B2B.")]
    [InlineData("See a%20b and v3.14 now.")]
    [InlineData("It weighs 2.5kg and 1.5m.")]
    [InlineData("A 50%off sale at 12:30ish.")]
    [InlineData("Model 1,234x and build 1.2.3a.")]
    [InlineData("Chapter600 and X-15b.")]
    [InlineData("No digits here.")]
    public void A_number_glued_to_letters_is_left_as_written(string text) =>
        Assert.Equal(text, EnglishNumbers.Spell(text));

    [Fact]
    public void A_number_after_a_hyphen_is_read() =>
        Assert.Equal("The COVID-nineteen years.", EnglishNumbers.Spell("The COVID-19 years."));

    /// <summary>The table's known limits, as read today. Each is a choice made to keep the rules context-free.</summary>
    [Theory]
    [InlineData("It sold 1850 copies.", "It sold eighteen fifty copies.")]          // a count in the year range is a year
    [InlineData("A 30s timeout.", "A thirties timeout.")]                           // seconds are never read
    [InlineData("John 3:16 says.", "John three sixteen says.")]                     // a verse reads as a time
    [InlineData("Half is 1/2.", "Half is one/two.")]                                // no fractions
    [InlineData("Call 555-1234.", "Call five hundred fifty-five to twelve thirty-four.")] // without an area code, a range
    [InlineData("It ended 10-5.", "It ended ten to five.")]                         // a score reads as a range
    [InlineData("Then at 7.30.", "Then at seven point three zero.")]                // a dotted time needs am/pm
    [InlineData("It was US$5.", "It was US five dollars.")]                         // country letters are read as written
    public void Known_limits_read_as_the_table_says(string text, string spoken) =>
        Assert.Equal(spoken, EnglishNumbers.Spell(text));

    [Theory]
    [InlineData(0, "zero")]
    [InlineData(74, "seventy-four")]
    [InlineData(115, "one hundred fifteen")]
    [InlineData(1174, "one thousand one hundred seventy-four")]
    [InlineData(2_003_004_005, "two billion three million four thousand five")]
    public void Cardinal_counts(long n, string words) =>
        Assert.Equal(words, EnglishNumbers.Cardinal(n));

    [Theory]
    [InlineData("PR #174", "PR 174")]
    [InlineData("#1 in the charts", "1 in the charts")]
    [InlineData("See issues #12 and #13.", "See issues 12 and 13.")]
    [InlineData("Play it in C#7.", "Play it in C#7.")]
    [InlineData("Use #hashtags and # signs.", "Use #hashtags and # signs.")]
    public void A_number_sign_before_a_number_is_not_read(string text, string spoken) =>
        Assert.Equal(spoken, SpeechPlan.WithoutNumberSign(text));

    [Theory]
    [InlineData("Merged PR #174 today.", "Merged PR one hundred seventy-four today.", new[] { "Merged", "PR", "#174", "today." })]
    [InlineData("Up 50%-60% now.", "Up fifty percent to sixty percent now.", new[] { "Up", "50%-60%", "now." })]
    public void A_plan_reads_the_numbers_out_but_lights_the_words_as_written(string text, string spoken, string[] words)
    {
        var piece = Assert.Single(SpeechPlan.Build(text));

        Assert.Equal(spoken, piece.Text);
        Assert.Equal(words, piece.Words.Select(w => text.Substring(w.Start, w.Length)));
    }
}
