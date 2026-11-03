using CheeseGit.Core.Repository;

namespace CheeseGit.Core.Tests;

public class CommitGraphTests
{
    [Fact]
    public async Task A_written_commit_graph_is_found_also_from_a_linked_worktree()
    {
        using var repo = TempRepo.WithCommits(3);
        using var linked = TempRepo.EmptyDirectory();
        var worktree = Path.Combine(linked.Path, "wt");
        repo.Raw("worktree", "add", "-q", "--detach", worktree);

        using (var git = await repo.OpenAsync())
        {
            Assert.True(await git.CommitGraphMissingAsync());
            await git.WriteCommitGraphAsync();
            Assert.False(await git.CommitGraphMissingAsync());
        }
        Assert.True(File.Exists(repo.FullPath(".git/objects/info/commit-graph")) || Directory.Exists(repo.FullPath(".git/objects/info/commit-graphs")));

        // The worktree's .git is a file; the objects live in the main repository.
        using var fromWorktree = await GitRepository.OpenAsync(worktree, repo.Git);
        Assert.False(await fromWorktree.CommitGraphMissingAsync());
    }

    [Fact]
    public async Task A_split_commit_graph_chain_counts()
    {
        using var repo = TempRepo.WithCommits(2);
        repo.Raw("commit-graph", "write", "--reachable", "--split");
        using var git = await repo.OpenAsync();
        Assert.False(await git.CommitGraphMissingAsync());
    }

    [Fact]
    public async Task Nothing_is_offered_when_git_would_not_use_it()
    {
        using var off = TempRepo.WithCommits(2);
        off.Raw("config", "core.commitGraph", "false");
        using (var git = await off.OpenAsync()) Assert.False(await git.CommitGraphMissingAsync());

        using var source = TempRepo.WithCommits(3);
        using var shallow = TempRepo.EmptyDirectory();
        shallow.Raw("clone", "-q", "--depth", "1", new Uri(source.Path).AbsoluteUri, shallow.Path);
        using (var git = await shallow.OpenAsync()) Assert.False(await git.CommitGraphMissingAsync());
    }
}
