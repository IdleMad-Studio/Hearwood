namespace Hearwood.Tests;

public class ConfigTests
{
    [Fact]
    public void The_speeds_run_from_three_quarters_to_triple_in_quarter_steps() =>
        Assert.Equal([0.75, 1.0, 1.25, 1.5, 1.75, 2.0, 2.25, 2.5, 2.75, 3.0], Config.SpeedChoices);

    [Theory]
    [InlineData(1.1, 1.0)]
    [InlineData(1.9, 2.0)]
    [InlineData(0.5, 0.75)]
    [InlineData(9.0, 3.0)]
    public void A_stored_speed_snaps_to_the_list(double stored, double expected) =>
        Assert.Equal(expected, Config.SnapRate(stored));

    [Theory]
    [InlineData(1.0, 1, 1.25)]
    [InlineData(1.0, -1, 0.75)]
    [InlineData(0.75, -1, 0.75)]
    [InlineData(3.0, 1, 3.0)]
    [InlineData(1.1, 1, 1.25)]
    public void Speed_steps_move_along_the_list_and_stop_at_its_ends(double from, int steps, double expected) =>
        Assert.Equal(expected, Config.StepRate(from, steps));

    [Fact]
    public void Every_listed_speed_is_within_what_the_stream_allows() =>
        Assert.All(Config.SpeedChoices, s => Assert.InRange(s, Config.MinRate, Config.MaxRate));
}
