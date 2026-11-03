using CheeseGit.Core.Graph;
using CheeseGit.Fixtures;

namespace CheeseGit.Core.Tests;

/// <summary>
/// The graph corpus (docs/testing.md, Graph corpus) through the real engine: every layout validates, and the rows
/// are exactly what git reaches from the visible branches and HEAD. App.Tests renders the same
/// corpus to images.
/// </summary>
public class GraphCorpusTests
{
    public static TheoryData<string> Cases => [.. GraphCorpus.Cases.Select(c => c.Name)];

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Corpus_history_validates_and_shows_every_reachable_commit(string name)
    {
        using var dir = TempRepo.EmptyDirectory();
        var fixture = await GraphCorpus.CreateAsync(GraphCorpus.Cases.Single(c => c.Name == name), dir.Path);
        var view = await fixture.LoadViewAsync(dir.Git);

        Assert.Null(LayoutValidator.Validate(view.Dag, view.Layout));
        var count = await dir.Git.RunCheckedAsync("count", fixture.Path, ["rev-list", "--count", .. view.VisibleBranches, "HEAD", "--"]);
        Assert.Equal(int.Parse(count.StdoutText.Trim()), view.Count);

        // The main branch's first-parent chain sits in lane 0.
        var mainTip = view.MainTip;
        Assert.True(mainTip >= 0);
        for (var row = mainTip; row >= 0; row = view.Dag.ParentsOf(row) is { Length: > 0 } p ? p[0] : -1)
        {
            Assert.Equal(0, view.Layout.NodeLane(row));
        }
    }
}
