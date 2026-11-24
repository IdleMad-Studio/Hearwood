namespace Hearwood.Tests;

public class LicencesTests
{
    // The build copies the licence files next to the app, and from there into the tests' output.
    [Fact]
    public void Every_file_the_licences_menu_opens_ships_with_the_app() =>
        Assert.All(Licences.Files, f => Assert.True(File.Exists(Licences.PathOf(f.File)), f.File + " is missing"));
}
