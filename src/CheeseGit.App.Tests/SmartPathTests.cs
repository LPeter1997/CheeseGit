using CheeseGit.App.Controls;

namespace CheeseGit.App.Tests;

public class SmartPathTests
{
    // One unit per character, so widths are character counts.
    private static string Shorten(string path, int width) => SmartPath.Shorten(path, width, s => s.Length);

    [Theory]
    [InlineData("a/b/c/File.cs", 13, "a/b/c/File.cs")]
    [InlineData("src/app/views/Main.cs", 17, "src/app/…/Main.cs")]
    [InlineData("src/app/views/Main.cs", 16, "src/…/Main.cs")]
    [InlineData("src/app/views/Main.cs", 10, "…/Main.cs")]
    public void Directories_go_first(string path, int width, string expected) => Assert.Equal(expected, Shorten(path, width));

    [Theory]
    [InlineData("dotnet/Views/HistoryListView.axaml.cs", 16, "HistoryLi…aml.cs")]
    [InlineData("ConflictDialogViewModel.cs", 16, "ConflictD…del.cs")]
    [InlineData("README", 5, "R…DME")]
    public void Long_names_lose_their_middle_and_keep_the_extension(string path, int width, string expected)
    {
        var shown = Shorten(path, width);
        Assert.Equal(expected, shown);
        Assert.DoesNotContain(".…", shown);
        Assert.DoesNotContain("….", shown);
    }
}
