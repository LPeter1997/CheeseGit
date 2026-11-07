using System.Diagnostics;
using System.Text;
using CheeseGit.Core.Git;
using CheeseGit.Core.Platform;
using CheeseGit.Core.Repository;

namespace CheeseGit.Core.Tests;

/// <summary>Phase 5 engine additions: keeping an edited resolution, the revert message, merge prediction, merge tools.</summary>
public class MergePhase5Tests
{
    private static TempRepo ConflictingBranches(string path = "file.txt")
    {
        var repo = TempRepo.Init();
        repo.CommitFile(path, "original\n", "initial");
        repo.Raw("switch", "-q", "-c", "feature");
        repo.CommitFile(path, "feature version\n", "feature");
        repo.Raw("switch", "-q", "-");
        repo.CommitFile(path, "main version\n", "main");
        return repo;
    }

    [Fact]
    public async Task Keeping_the_working_tree_stages_the_users_edit_byte_for_byte()
    {
        using var repo = ConflictingBranches();
        using var git = await repo.OpenAsync();
        Assert.IsType<MergeResult.Conflict>(await git.MergeAsync("feature"));
        // Edited in an editor: neither side, no markers, CRLF and no final newline.
        repo.Write("file.txt", "hand made\r\nresult");
        await git.ResolveConflictAsync("file.txt", ConflictResolution.KeepWorkingTree);
        Assert.Empty(await git.ConflictedFilesAsync());
        Assert.Equal("hand made\r\nresult", repo.Read("file.txt"));
        await git.MergeContinueAsync("Merge feature");
        // The commit holds the edit (after the user's CRLF filters): nothing differs from HEAD.
        Assert.Equal(0, repo.TryRaw("diff", "--quiet", "HEAD", "--", "file.txt").ExitCode);
        Assert.Contains("hand made", repo.Raw("show", "HEAD:file.txt"));
    }

    [Fact]
    public async Task Keeping_a_deleted_file_resolves_as_a_deletion()
    {
        using var repo = ConflictingBranches();
        using var git = await repo.OpenAsync();
        Assert.IsType<MergeResult.Conflict>(await git.MergeAsync("feature"));
        repo.Delete("file.txt");
        await git.ResolveConflictAsync("file.txt", ConflictResolution.KeepWorkingTree);
        Assert.Empty(await git.ConflictedFilesAsync());
        await git.MergeContinueAsync("Merge feature");
        Assert.Equal("", repo.Raw("ls-tree", "--name-only", "HEAD").Trim());
    }

    [Fact]
    public async Task Merge_commits_are_named_after_the_short_branch_name()
    {
        using var origin = TempRepo.Init();
        origin.CommitFile("a.txt", "a\n", "initial");
        using var repo = TempRepo.CloneOf(origin);
        var main = repo.CurrentBranch();
        repo.Raw("switch", "-q", "-c", "feature/x");
        repo.CommitFile("b.txt", "b\n", "feature");
        repo.Raw("switch", "-q", main);
        repo.CommitFile("c.txt", "c\n", "main");
        origin.CommitFile("d.txt", "d\n", "upstream");
        repo.Raw("fetch", "-q");
        using var git = await repo.OpenAsync();

        Assert.IsType<MergeResult.Success>(await git.MergeAsync("feature/x"));
        Assert.Matches(@"^Merge branch 'feature/x'( into .*)?$", repo.Raw("log", "-1", "--format=%s").Trim());
        Assert.IsType<MergeResult.Success>(await git.MergeAsync($"origin/{main}"));
        Assert.Matches($@"^Merge remote-tracking branch 'origin/{main}'( into .*)?$", repo.Raw("log", "-1", "--format=%s").Trim());

        // With conflicts git prepares the same name for the commit the user completes.
        using var conflicting = ConflictingBranches();
        using var git2 = await conflicting.OpenAsync();
        Assert.IsType<MergeResult.Conflict>(await git2.MergeAsync("feature"));
        Assert.StartsWith("Merge branch 'feature'", await git2.PreparedMessageAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Revert_continue_commits_the_given_message()
    {
        using var repo = TempRepo.Init();
        repo.CommitFile("file.txt", "line1\nline2\nline3\n", "initial commit");
        var toRevert = Oid.ParseHex(repo.CommitFile("file.txt", "line1\nchanged\nline3\n", "modify line2"));
        repo.CommitFile("file.txt", "line1\ndifferent\nline3\n", "modify again");
        using var git = await repo.OpenAsync();
        Assert.IsType<RevertResult.Conflict>(await git.RevertAsync(toRevert));
        await git.ResolveConflictAsync("file.txt", ConflictResolution.AcceptIncoming);
        await git.RevertContinueAsync("Undo the line2 change\n\nIt broke things.");
        Assert.Equal(OperationKind.None, (await git.OperationStateAsync()).Kind);
        Assert.Equal("Undo the line2 change\n\nIt broke things.", repo.Raw("log", "-1", "--format=%B").Trim());
    }

    [Fact]
    public async Task Prediction_finds_conflicts_without_touching_anything()
    {
        if (!(await GitVersion.DetectAsync(new GitRunner())).SupportsMergeTreeWriteTree) return;
        using var repo = ConflictingBranches("dir/with space.txt");
        repo.Raw("switch", "-q", "feature");
        repo.CommitFile("only-feature.txt", "x\n", "feature only");
        repo.Raw("switch", "-q", "-");
        repo.Write("dir/with space.txt", "local edit\n");
        using var git = await repo.OpenAsync();
        var head = repo.Head();

        var conflicts = Assert.IsType<MergePrediction.Conflicts>(await git.PredictMergeAsync("feature"));
        Assert.Equal([GitPath.FromString("dir/with space.txt")], conflicts.Files);
        Assert.Equal(head, repo.Head());
        Assert.Equal("local edit\n", repo.ReadNormalized("dir/with space.txt"));
        Assert.Equal(OperationKind.None, (await git.OperationStateAsync()).Kind);
    }

    [Fact]
    public async Task Prediction_of_clean_and_contained_branches()
    {
        if (!(await GitVersion.DetectAsync(new GitRunner())).SupportsMergeTreeWriteTree) return;
        using var repo = TempRepo.Init();
        repo.CommitFile("a.txt", "a\n", "initial");
        repo.Raw("branch", "old");
        repo.Raw("switch", "-q", "-c", "side");
        repo.CommitFile("b.txt", "b\n", "side");
        repo.Raw("switch", "-q", "-");
        repo.CommitFile("c.txt", "c\n", "main");
        using var git = await repo.OpenAsync();
        Assert.IsType<MergePrediction.Clean>(await git.PredictMergeAsync("side"));
        Assert.IsType<MergePrediction.UpToDate>(await git.PredictMergeAsync("old"));
        await Assert.ThrowsAsync<GitOperationException>(() => git.PredictMergeAsync("missing"));
    }

    // ── Merge tools ──────────────────────────────────────────────────

    [Fact]
    public void Only_installed_tools_are_offered_and_the_preference_wins()
    {
        var installed = new HashSet<string> { "meld", "code" };
        var registry = new MergeToolRegistry(findExecutable: n => installed.Contains(n) ? "/bin/" + n : null);
        Assert.Equal(["vscode", "meld"], registry.Available().Select(t => t.Id));
        Assert.Equal("meld", registry.Pick("meld")!.Id);
        Assert.Equal("vscode", registry.Pick("kdiff3")!.Id);
        Assert.Null(new MergeToolRegistry(findExecutable: _ => null).Pick(null));
    }

    [Fact]
    public void Terminal_tools_need_a_terminal_on_linux()
    {
        if (!OperatingSystem.IsLinux()) return;
        Assert.DoesNotContain(new MergeToolRegistry(findExecutable: n => n == "nvim" ? "/bin/nvim" : null).Available(), t => t.Id == "neovim");
        var withTerminal = new MergeToolRegistry(findExecutable: n => n is "nvim" or "konsole" ? "/bin/" + n : null);
        Assert.Contains(withTerminal.Available(), t => t.Id == "neovim");
    }

    [Fact]
    public async Task Opening_a_tool_hands_it_the_three_versions_and_the_working_tree_file()
    {
        using var repo = ConflictingBranches("src/app.cs");
        using var git = await repo.OpenAsync();
        Assert.IsType<MergeResult.Conflict>(await git.MergeAsync("feature"));
        var started = new List<ProcessStartInfo>();
        var registry = new MergeToolRegistry(findExecutable: n => n == "code" ? "/usr/bin/code" : null, start: started.Add);

        var psi = await registry.OpenAsync(registry.Pick(null)!, git, "src/app.cs");
        Assert.Same(psi, Assert.Single(started));
        Assert.Equal("/usr/bin/code", psi.FileName);
        Assert.Equal(repo.Path, psi.WorkingDirectory);
        var args = psi.ArgumentList;
        Assert.Equal(["--wait", "--merge"], args.Take(2));
        Assert.EndsWith("app.LOCAL.cs", args[2], StringComparison.Ordinal);
        Assert.Equal("main version\n", File.ReadAllText(args[2]).Replace("\r\n", "\n"));
        Assert.Equal("feature version\n", File.ReadAllText(args[3]).Replace("\r\n", "\n"));
        Assert.Equal("original\n", File.ReadAllText(args[4]).Replace("\r\n", "\n"));
        Assert.Equal(repo.FullPath(Path.Combine("src", "app.cs")), args[5]);
    }

    [Fact]
    public async Task A_file_added_on_both_sides_opens_without_an_empty_base()
    {
        // No stage 1: an empty base would make the tool show the whole file as one conflict with
        // an empty result (seen in VS Code), when git's markers hold just the lines that differ.
        using var repo = TempRepo.Init();
        repo.CommitFile("seed.txt", "seed\n", "initial");
        repo.Raw("switch", "-q", "-c", "feature");
        repo.CommitFile("same.cs", "one\nfeature\nthree\n", "feature");
        repo.Raw("switch", "-q", "-");
        repo.CommitFile("same.cs", "one\nmain\nthree\n", "main");
        using var git = await repo.OpenAsync();
        Assert.IsType<MergeResult.Conflict>(await git.MergeAsync("feature"));
        Assert.Null((await git.ConflictStagesAsync("same.cs")).Base);

        var started = new List<ProcessStartInfo>();
        var registry = new MergeToolRegistry(findExecutable: n => "/bin/" + n, start: started.Add);
        var merged = repo.FullPath("same.cs");
        var code = await registry.OpenAsync(registry.All.Single(t => t.Id == "vscode"), git, "same.cs");
        Assert.Equal([merged], code.ArgumentList);

        // Every other tool that can do without a base gets the two sides and the output.
        foreach (var tool in registry.Available().Where(t => t.Id is not ("vscode" or "visualstudio")))
        {
            var args = (await registry.OpenAsync(tool, git, "same.cs")).ArgumentList;
            Assert.DoesNotContain(args, a => a.EndsWith("same.BASE.cs", StringComparison.Ordinal));
            Assert.Contains(args, a => a.EndsWith("same.LOCAL.cs", StringComparison.Ordinal));
            Assert.Contains(args, a => a.EndsWith("same.REMOTE.cs", StringComparison.Ordinal));
            Assert.Contains(args, a => a.Contains(merged, StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task Stages_of_a_later_conflict_are_not_read_from_an_earlier_index()
    {
        // cat-file reads the index once, at its first ":N:path": a later conflict came back empty.
        using var repo = ConflictingBranches();
        repo.CommitFile("other.txt", "other original\n", "other");
        repo.Raw("switch", "-q", "-c", "second");
        repo.CommitFile("other.txt", "second version\n", "second");
        repo.Raw("switch", "-q", "-");
        repo.CommitFile("other.txt", "main other\n", "main other");
        using var git = await repo.OpenAsync();

        Assert.IsType<MergeResult.Conflict>(await git.MergeAsync("feature"));
        Assert.NotNull((await git.ConflictStagesAsync("file.txt")).Ours);
        repo.Raw("merge", "--abort");

        Assert.IsType<MergeResult.Conflict>(await git.MergeAsync("second"));
        var stages = await git.ConflictStagesAsync("other.txt");
        Assert.Equal("other original\n", Encoding.UTF8.GetString(stages.Base!).Replace("\r\n", "\n"));
        Assert.Equal("main other\n", Encoding.UTF8.GetString(stages.Ours!).Replace("\r\n", "\n"));
        Assert.Equal("second version\n", Encoding.UTF8.GetString(stages.Theirs!).Replace("\r\n", "\n"));

        await git.ResolveConflictAsync("other.txt", ConflictResolution.AcceptBoth);
        var both = repo.Read("other.txt");
        Assert.Contains("main other", both);
        Assert.Contains("second version", both);
    }

    [Fact]
    public void Path_lookup_finds_executables_only()
    {
        if (OperatingSystem.IsWindows()) return;
        // "sh" exists on every Unix; a directory of the same name as a tool does not count.
        Assert.NotNull(MergeToolRegistry.FindOnPath("sh"));
        Assert.Null(MergeToolRegistry.FindOnPath("cheesegit-no-such-tool"));
    }

    // ── Cherry-pick sequences ─────────────────────────────────────────

    /// <summary>"source" has three commits on top of the start: a.txt, b.txt (conflicts with target), c.txt.</summary>
    private static (TempRepo Repo, Oid[] Picks) PickSource()
    {
        var repo = TempRepo.Init();
        repo.CommitFile("b.txt", "base\n", "initial");
        repo.Raw("switch", "-q", "-c", "target");
        repo.CommitFile("b.txt", "target version\n", "target edit");
        repo.Raw("switch", "-q", "-c", "source", "HEAD~1");
        var a = repo.CommitFile("a.txt", "a\n", "add a");
        var b = repo.CommitFile("b.txt", "source version\n", "edit b");
        var c = repo.CommitFile("c.txt", "c\n", "add c");
        return (repo, [Oid.ParseHex(a), Oid.ParseHex(b), Oid.ParseHex(c)]);
    }

    [Fact]
    public async Task A_conflict_mid_sequence_keeps_earlier_picks_and_continue_picks_the_rest()
    {
        var (repo, picks) = PickSource();
        using var _ = repo;
        using var git = await repo.OpenAsync();

        var conflict = Assert.IsType<CherryPickResult.Conflict>(await git.CherryPickAsync(picks, "target", createBranch: false));
        Assert.Equal(picks[1].ToShortHex(), conflict.Info.IncomingBranch);
        Assert.Equal("target", await git.CurrentBranchAsync());
        Assert.True(repo.Exists("a.txt"));
        Assert.False(repo.Exists("c.txt"));

        repo.Write("b.txt", "both\n");
        await git.ResolveConflictAsync("b.txt", ConflictResolution.KeepWorkingTree);
        Assert.True(await git.ContinueOperationAsync(OperationKind.CherryPick, "edit b (merged)"));

        Assert.Equal(OperationKind.None, (await git.OperationStateAsync()).Kind);
        Assert.Equal(["add c", "edit b (merged)", "add a", "target edit"], repo.Raw("log", "--format=%s", "-4").Trim().Split('\n'));
        // -x records where each pick came from.
        Assert.Contains("cherry picked from commit " + picks[2].ToHex(), repo.Raw("log", "-1", "--format=%B"));
    }

    [Fact]
    public async Task Continuing_reports_a_later_commit_that_conflicts_too()
    {
        var (repo, picks) = PickSource();
        using var _ = repo;
        repo.Raw("switch", "-q", "target");
        repo.CommitFile("c.txt", "target c\n", "target c");
        using var git = await repo.OpenAsync();

        Assert.IsType<CherryPickResult.Conflict>(await git.CherryPickAsync(picks, "target", createBranch: false));
        await git.ResolveConflictAsync("b.txt", ConflictResolution.AcceptIncoming);
        Assert.False(await git.ContinueOperationAsync(OperationKind.CherryPick, "edit b"));

        var state = await git.OperationStateAsync();
        Assert.Equal(OperationKind.CherryPick, state.Kind);
        Assert.Equal(["c.txt"], (await git.ConflictedFilesAsync()).Select(f => f.ToString()));
        await git.AbortOperationAsync(OperationKind.CherryPick);
        Assert.Equal(OperationKind.None, (await git.OperationStateAsync()).Kind);
    }

    [Fact]
    public async Task Picks_already_on_the_branch_are_skipped()
    {
        var (repo, picks) = PickSource();
        using var _ = repo;
        repo.Raw("switch", "-q", "-c", "has-a", "target");
        repo.CommitFile("a.txt", "a\n", "same change as add a");
        using var git = await repo.OpenAsync();

        var success = Assert.IsType<CherryPickResult.Success>(await git.CherryPickAsync([picks[0], picks[2]], "has-a", createBranch: false));
        Assert.Equal(1, success.Picked);
        Assert.Equal(OperationKind.None, (await git.OperationStateAsync()).Kind);
        Assert.Equal("add c", repo.Raw("log", "-1", "--format=%s").Trim());
    }

    [Fact]
    public async Task Local_changes_in_the_way_leave_no_cherry_pick_in_progress()
    {
        var (repo, picks) = PickSource();
        using var _ = repo;
        repo.Raw("switch", "-q", "target");
        using var git = await repo.OpenAsync();
        // Untracked c.txt would be overwritten by the third pick.
        repo.Write("c.txt", "mine\n");

        await Assert.ThrowsAsync<GitCommandException>(() => git.CherryPickAsync([picks[0], picks[2]], "target", createBranch: false));
        Assert.Equal(OperationKind.None, (await git.OperationStateAsync()).Kind);
        Assert.Equal("mine\n", repo.ReadNormalized("c.txt"));
        // The sequencer is gone: another cherry-pick is not refused as "in progress".
        Assert.False(Directory.Exists(System.IO.Path.Combine(repo.Raw("rev-parse", "--absolute-git-dir").Trim(), "sequencer")));
    }
}
