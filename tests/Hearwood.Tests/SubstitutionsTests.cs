using Hearwood.Text;

namespace Hearwood.Tests;

public class SubstitutionsTests
{
    static string Apply(string text, params SubstitutionRule[] rules) => new Substitutions(rules).Apply(text);

    [Fact]
    public void A_rule_matches_whole_words_only() =>
        Assert.Equal("It aked, but she reached it.", Apply("It ached, but she reached it.", new SubstitutionRule("ached", "aked")));

    [Fact]
    public void A_caseless_rule_keeps_a_leading_capital_but_not_all_caps()
    {
        var rule = new SubstitutionRule("hegemony", "hedgemony");
        Assert.Equal("Hedgemony and hedgemony and hedgemony", Apply("Hegemony and hegemony and HEGEMONY", rule));
    }

    [Fact]
    public void A_match_case_rule_leaves_other_cases_alone() =>
        Assert.Equal("em ti ey, but mta", Apply("MTA, but mta", new SubstitutionRule("MTA", "em ti ey", MatchCase: true)));

    [Fact]
    public void An_exact_case_rule_wins_over_its_caseless_twin() =>
        Assert.Equal("polish it, the Poalish", Apply("polish it, the Polish",
            new SubstitutionRule("polish", "polish"), new SubstitutionRule("Polish", "Poalish", MatchCase: true)));

    [Fact]
    public void The_longest_rule_wins() =>
        Assert.Equal("gosh darn it", Apply("goddamnit", new SubstitutionRule("damnit", "darn it"), new SubstitutionRule("goddamnit", "gosh darn it")));

    [Fact]
    public void A_replacement_is_never_substituted_again() =>
        Assert.Equal("b c", Apply("a b", new SubstitutionRule("a", "b"), new SubstitutionRule("b", "c")));

    [Fact]
    public void A_rule_ending_in_punctuation_matches_inside_a_word_and_is_spaced_from_it() =>
        Assert.Equal("ASP dot net and dot net", Apply("ASP.NET and .NET", new SubstitutionRule(".NET", "dot net")));

    [Fact]
    public void Symbols_in_a_rule_are_matched_literally() =>
        Assert.Equal("I like C plus plus. Not CXX.", Apply("I like C++. Not CXX.", new SubstitutionRule("C++", "C plus plus")));

    [Theory]
    [InlineData("don't")]
    [InlineData("don’t")]
    public void Straight_and_curly_apostrophes_match_each_other(string rule) =>
        Assert.Equal("do not, do not", Apply("don't, don’t", new SubstitutionRule(rule, "do not")));

    [Fact]
    public void An_empty_replacement_removes_the_word() =>
        Assert.Equal("So, I think", Apply("So, um, I think", new SubstitutionRule("um,", "")).Replace("  ", " "));

    [Fact]
    public void No_rules_change_nothing() =>
        Assert.Equal("Anything at all.", Substitutions.Empty.Apply("Anything at all."));

    [Fact]
    public void The_file_round_trips_in_the_listeneasy_format()
    {
        var path = TempFile();
        SubstitutionRule[] rules = [new SubstitutionRule("XAML", "zammel"), new SubstitutionRule("MTA", "em ti ey", MatchCase: true), new SubstitutionRule("Kyiv", "Keev")];
        Substitutions.Write(path, rules);

        var json = File.ReadAllText(path);
        Assert.Contains("\"format\": \"listeneasy-substitutions\"", json);
        Assert.Contains("\"XAML\": \"zammel\"", json);
        Assert.Contains("\"case\": true", json);
        Assert.Equal(rules, Substitutions.Read(path));
    }

    [Fact]
    public void Text_beyond_ascii_is_written_as_it_is()
    {
        var path = TempFile();
        Substitutions.Write(path, [new SubstitutionRule("café", "ka-fay")]);
        Assert.Contains("\"café\"", File.ReadAllText(path));
    }

    [Theory]
    [InlineData("{\"substitutions\": {\"XAML\": \"zammel\"}}", 1)]  // no format given
    [InlineData("﻿{\"format\": \"listeneasy-substitutions\", \"substitutions\": {}}", 0)]
    public void A_file_without_a_format_or_with_a_bom_is_read(string json, int count)
    {
        var path = TempFile();
        File.WriteAllText(path, json);
        Assert.Equal(count, Substitutions.Read(path).Count);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{\"format\": \"something-else\", \"substitutions\": {}}")]
    [InlineData("{\"format\": \"listeneasy-substitutions\"}")]
    [InlineData("{\"substitutions\": {\"XAML\": 5}}")]
    [InlineData("{\"substitutions\": {\"XAML\": {\"case\": true}}}")]
    public void Other_files_are_refused(string json)
    {
        var path = TempFile();
        File.WriteAllText(path, json);
        Assert.Throws<InvalidDataException>(() => Substitutions.Read(path));
    }

    [Fact]
    public void Without_a_file_there_are_no_substitutions() =>
        Assert.Empty(Substitutions.Load(TempFile()).Rules);

    [Theory]
    [InlineData("Say “XAML,” please", 4, "XAML")]
    [InlineData("Built on ASP.NET.", 9, "ASP.NET")]
    [InlineData("It runs .NET", 8, ".NET")]
    [InlineData("(Kyiv)", 0, "Kyiv")]
    [InlineData("don't", 0, "don't")]
    [InlineData("end ", 4, "")]
    public void The_word_offered_for_a_substitution_drops_the_punctuation_around_it(string text, int start, string expected) =>
        Assert.Equal(expected, Substitutions.WordAt(text, start));

    [Fact]
    public void An_import_merged_in_replaces_rules_for_the_same_word_in_place()
    {
        SubstitutionRule[] current = [new SubstitutionRule("a", "1"), new SubstitutionRule("b", "2")];
        SubstitutionRule[] imported = [new SubstitutionRule("b", "3"), new SubstitutionRule("c", "4")];
        Assert.Equal([new SubstitutionRule("a", "1"), new SubstitutionRule("b", "3"), new SubstitutionRule("c", "4")], SubstitutionsDialog.Merge(current, imported));
    }

    static string TempFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), "hearwood-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "substitutions.json");
    }
}
