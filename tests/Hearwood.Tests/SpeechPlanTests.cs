using Hearwood.Text;

namespace Hearwood.Tests;

/// <summary>Sentence ends, pauses and pieces, ported from another project by the same author, plus word positions for highlighting.</summary>
public class SpeechPlanTests
{
    [Theory]
    [InlineData("It cost a lot. $5 was left.", "It cost a lot.", "$5 was left.")]
    [InlineData("He stopped. — Then he ran.", "He stopped.", "— Then he ran.")]
    [InlineData("See below. * The first item.", "See below.", "* The first item.")]
    [InlineData("That was all. # Notes here.", "That was all.", "# Notes here.")]
    public void A_sentence_starting_with_a_symbol_is_split(string text, string first, string second) =>
        Assert.Equal([first, second], SpeechPlan.SplitSentences(text));

    [Theory]
    [InlineData("They met at 3 p.m. today.")]
    [InlineData("Ali vs. Frazier was the fight.")]
    [InlineData("Some cities, e.g. Paris, are big.")]
    [InlineData("The seat, i.e. The Hague, is small.")]
    [InlineData("DR. Smith came home.")]
    [InlineData("MR. JONES CAME HOME.")]
    [InlineData("Well... maybe not.")]
    [InlineData("I bought one. iPhones are dear.")]
    [InlineData("Mr. Harlow walked up the hill.")]
    [InlineData("The U.S.S. Merrimack had stopped.")]
    [InlineData("J. Smith came home.")]
    [InlineData("\"Dr. Quill is late.\"")]
    [InlineData("It cost 3.50 Dollars.")]
    public void Abbreviations_and_numbers_do_not_end_a_sentence(string text) =>
        Assert.Equal(text, Assert.Single(SpeechPlan.SplitSentences(text)));

    [Fact]
    public void A_title_ending_a_sentence_still_needs_a_lower_case_word_to_carry_on()
    {
        Assert.False(SentenceRules.EndsSentence("Dr.", "Smith"));
        Assert.True(SentenceRules.EndsSentence("home.", "Then"));
        Assert.False(SentenceRules.EndsSentence("\"No!\"", "he"));
        Assert.True(SentenceRules.EndsSentence("end.", null));
        Assert.False(SentenceRules.EndsSentence("Chapter One", null));
    }

    [Fact]
    public void Sentences_split_at_their_ends() =>
        Assert.Equal(
            ["He left.", "Was she there?", "\"No!\" he said.", "22 men came."],
            SpeechPlan.SplitSentences("He left. Was she there? \"No!\" he said. 22 men came."));

    [Fact]
    public void A_piece_with_nothing_to_say_is_dropped()
    {
        Assert.Empty(SpeechPlan.SplitSentences("* * *"));
        Assert.Empty(SpeechPlan.Build("* * *\n\n  "));
    }

    [Theory]
    [InlineData("It ended.", false, SpeechPlan.SentencePause)]
    [InlineData("\"It ended?\"", false, SpeechPlan.SentencePause)]
    [InlineData("it went on,", false, SpeechPlan.ClausePause)]
    [InlineData("no mark", false, 0f)]
    [InlineData("It ended.", true, SpeechPlan.ParagraphPause)]
    [InlineData("Chapter 1", true, SpeechPlan.ParagraphPause)]
    public void Pauses_follow_the_punctuation(string text, bool endsParagraph, float expected) =>
        Assert.Equal(expected, SpeechPlan.PauseAfter(text, endsParagraph));

    [Fact]
    public void Words_are_shared_between_pieces_and_none_are_lost()
    {
        string[] words = ["one", "two", "three", "four", "five"];

        var exact = SpeechPlan.ShareWords(words, ["one two", "three four five"]);
        Assert.Equal(["one", "two"], exact[0]);
        Assert.Equal(["three", "four", "five"], exact[1]);

        var off = SpeechPlan.ShareWords(words, ["one two three", "four five six seven"]);
        Assert.Equal(words, off.SelectMany(w => w));
    }

    [Theory]
    [InlineData("Chapter One", "Chapter One.")]
    [InlineData("Table 2", "Table 2.")]
    [InlineData("It ended.", "It ended.")]
    [InlineData("Was it?", "Was it?")]
    [InlineData("\"Go home\"", "\"Go home\"")]
    public void A_sentence_without_a_stop_is_given_one(string sentence, string spoken) =>
        Assert.Equal(spoken, Assert.Single(SpeechPlan.Build(sentence)).Text);

    [Fact]
    public void A_sentence_with_no_ends_goes_in_bounded_pieces_and_only_the_last_gets_the_stop()
    {
        // A contents page from a PDF: thousands of characters and no sentence end.
        var words = Enumerable.Range(1, 600).Select(i => $"Chapter{i}").ToList();

        var pieces = SpeechPlan.Build(string.Join(' ', words));

        Assert.True(pieces.Count > 1);
        Assert.All(pieces, p => Assert.True(p.Text.Length <= SpeechPlan.MaxPieceLength + 1));
        Assert.All(pieces.SkipLast(1), p => Assert.False(p.Text.EndsWith('.')));
        Assert.EndsWith("Chapter600.", pieces[^1].Text);
        Assert.Equal(words, pieces.SelectMany(p => p.Text.TrimEnd('.').Split(' ')));
    }

    [Fact]
    public void Every_word_points_at_itself_in_the_source()
    {
        const string text = "  The keeper, Mr. Harlow, climbed.\r\n\r\n\"No!\" he said —  twice.\nEnd";

        var pieces = SpeechPlan.Build(text);

        var spans = pieces.SelectMany(p => p.Words).ToList();
        Assert.Equal(
            ["The", "keeper,", "Mr.", "Harlow,", "climbed.", "\"No!\"", "he", "said", "—", "twice.", "End"],
            spans.Select(w => text.Substring(w.Start, w.Length)));
        Assert.True(spans.Zip(spans.Skip(1)).All(p => p.First.Start < p.Second.Start));
    }

    [Fact]
    public void Paragraphs_pause_longer_than_sentences()
    {
        var pieces = SpeechPlan.Build("One. Two.\n\nThree.");

        Assert.Equal([SpeechPlan.SentencePause, SpeechPlan.ParagraphPause, SpeechPlan.SentencePause], pieces.Select(p => p.PauseAfter));
    }

    [Fact]
    public void The_word_heard_moves_through_the_piece_by_weight()
    {
        var words = SpeechPlan.Build("a extraordinary 1847").Single().Words;

        Assert.Equal(words[0], SpeechPlan.WordAt(words, 0, 10));
        Assert.Equal(words[1], SpeechPlan.WordAt(words, 3, 10));
        Assert.Equal(words[2], SpeechPlan.WordAt(words, 9.99, 10));
        Assert.Equal(words[2], SpeechPlan.WordAt(words, 50, 10));
        Assert.Null(SpeechPlan.WordAt([], 1, 10));
    }

    [Theory]
    [InlineData("Prices rose by 9.99% last year.", "Prices rose by 9.99 percent last year.")]
    [InlineData("Up 12%.", "Up 12 percent.")]
    [InlineData("It fell 3.5 % today.", "It fell 3.5 percent today.")]
    [InlineData("(about 40%)", "(about 40 percent)")]
    [InlineData("It rose 12%", "It rose 12 percent.")]
    [InlineData("Go to example.com/a%20b now.", "Go to example.com/a%20b now.")]
    [InlineData("Use x1%2F here.", "Use x1%2F here.")]
    [InlineData("Use % here.", "Use % here.")]
    public void A_percent_sign_after_a_number_is_spoken(string text, string spoken) =>
        Assert.Equal(spoken, SpeechPlan.Build(text).Single().Text);

    [Fact]
    public void A_percent_sign_keeps_its_source_word_and_weighs_like_the_word()
    {
        var words = SpeechPlan.Build("Up 9.99% now.").Single().Words;

        Assert.Equal(["Up", "9.99%", "now."], words.Select(w => "Up 9.99% now."[w.Start..(w.Start + w.Length)]));
        Assert.Equal(SpeechPlan.Weight("9.99") + SpeechPlan.Weight("percent"), SpeechPlan.Weight("9.99%"));
        Assert.Equal(SpeechPlan.Weight("percent"), SpeechPlan.Weight("%"));
    }

    [Fact]
    public void Longer_words_and_numbers_weigh_more()
    {
        Assert.True(SpeechPlan.Weight("extraordinary") > SpeechPlan.Weight("a"));
        Assert.True(SpeechPlan.Weight("1847") > SpeechPlan.Weight("a") * 3);
    }

    [Fact]
    public void Reading_from_a_word_starts_there_and_keeps_offsets_in_the_whole_text()
    {
        const string text = "First sentence here. Second one follows.\nA new line.";
        int from = text.IndexOf("one", StringComparison.Ordinal);

        var pieces = SpeechPlan.Build(text, from..);

        Assert.StartsWith("one follows.", pieces[0].Text);
        var words = pieces.SelectMany(p => p.Words).Select(w => text.Substring(w.Start, w.Length));
        Assert.Equal(["one", "follows.", "A", "new", "line."], words);
    }

    [Fact]
    public void Reading_from_the_start_is_the_whole_plan()
    {
        const string text = "One. Two three.";
        Assert.Equal(SpeechPlan.Build(text).Select(p => p.Text), SpeechPlan.Build(text, Range.All).Select(p => p.Text));
    }

    [Fact]
    public void Substitutions_change_what_is_said_but_words_keep_their_place_in_the_text()
    {
        const string text = "I write XAML daily.";
        var subs = new Substitutions([new SubstitutionRule("XAML", "zammel")]);

        var piece = Assert.Single(SpeechPlan.Build(text, subs.Apply));

        Assert.Equal("I write zammel daily.", piece.Text);
        Assert.Equal(["I", "write", "XAML", "daily."], piece.Words.Select(w => text.Substring(w.Start, w.Length)));
    }

    [Fact]
    public void A_sentence_substituted_away_is_not_spoken_but_its_pause_stays()
    {
        // "Um." ends the paragraph, so the paragraph's pause moves to the sentence before it.
        const string text = "Hi there. Um.\nNext one.";
        var subs = new Substitutions([new SubstitutionRule("Um.", "")]);

        var pieces = SpeechPlan.Build(text, subs.Apply);

        Assert.Equal(["Hi there.", "Next one."], pieces.Select(p => p.Text));
        Assert.Equal(SpeechPlan.ParagraphPause, pieces[0].PauseAfter);
        Assert.Equal(SpeechPlan.SentencePause, SpeechPlan.Build(text)[0].PauseAfter);
    }

    [Fact]
    public void Reading_a_selection_stops_at_its_end()
    {
        const string text = "Before it. The chosen words here. After it.";
        int from = text.IndexOf("chosen", StringComparison.Ordinal);
        int to = text.IndexOf(" here", StringComparison.Ordinal);

        var pieces = SpeechPlan.Build(text, from..to);

        var piece = Assert.Single(pieces);
        Assert.Equal("chosen words.", piece.Text);
        Assert.Equal(["chosen", "words"], piece.Words.Select(w => text.Substring(w.Start, w.Length)));
    }

    [Theory]
    [InlineData("alpha beta gamma", 8, 6)]   // inside "beta"
    [InlineData("alpha beta gamma", 6, 6)]   // on its first letter
    [InlineData("alpha beta gamma", 5, 6)]   // on the space before it
    [InlineData("alpha beta gamma", 0, 0)]
    [InlineData("alpha beta gamma", 16, 16)] // past the last word
    [InlineData("alpha\n\nbeta", 6, 7)]      // on a blank line: the next paragraph
    [InlineData("alpha   ", 6, 8)]           // trailing space: nothing follows
    [InlineData("", 0, 0)]
    public void Read_from_here_starts_at_the_word_clicked_or_the_next_one(string text, int clicked, int expected) =>
        Assert.Equal(expected, SpeechPlan.WordStartAt(text, clicked));
}
