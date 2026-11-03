using System.Text;
using CheeseGit.Core.Git;

namespace CheeseGit.Core.Tests;

public class GitRunnerTests
{
    // Names that need quoting, escaping or byte-exact handling somewhere.
    internal static readonly string[] HostileNames =
    [
        "plain.txt",
        "with space.txt",
        "ünïcödé ✓.txt",
        "quote\"and'apostrophe.txt",
        "-starts-with-dash.txt",
        "dir with space/nested file.txt",
        "tab\there.txt",
        "glob*.txt",
        "back\\slash.txt",
        " leading space.txt",
    ];

    internal static string[] HostileNamesForThisOs =>
        OperatingSystem.IsWindows() ? HostileNames.Where(n => n.IndexOfAny(['"', '\t', '*', '\\']) < 0).ToArray() : HostileNames;

    [Fact]
    public async Task Status_reports_hostile_paths_byte_exact_despite_hostile_config()
    {
        using var repo = TempRepo.Init();
        var names = HostileNamesForThisOs;
        foreach (var n in names) repo.Write(n, "v1\n");
        repo.CommitAll("init");
        foreach (var n in names) repo.Write(n, "v2\n");
        repo.Raw("mv", "--", "plain.txt", "renamed plain.txt");
        repo.Write("untracked ✓.txt", "new\n");

        var result = await repo.Git.RunCheckedAsync("status", repo.Path, StatusParser.Args);
        var status = StatusParser.Parse(result.Stdout);

        Assert.Equal(repo.CurrentBranch(), status.Branch.Branch);
        var paths = status.Entries.Select(e => e.Path.ToString()).ToHashSet();
        foreach (var n in names.Where(n => n != "plain.txt")) Assert.Contains(n, paths);
        Assert.Contains("untracked ✓.txt", paths);
        if (!TestEnvironment.Has("status.renames=false"))
        {
            var rename = Assert.Single(status.Entries, e => e.Kind == EntryKind.Renamed);
            Assert.Equal("renamed plain.txt", rename.Path.ToString());
            Assert.Equal("plain.txt", rename.OriginalPath.ToString());
        }
    }

    [Fact]
    public async Task Status_reports_merge_conflicts_as_unmerged()
    {
        using var repo = TempRepo.Init();
        repo.CommitFile("f.txt", "base\n", "base");
        var main = repo.CurrentBranch();
        repo.Raw("switch", "-q", "-c", "other");
        repo.CommitFile("f.txt", "theirs\n", "theirs");
        repo.Raw("switch", "-q", main);
        repo.CommitFile("f.txt", "ours\n", "ours");
        var merge = await repo.Git.RunAsync(repo.Path, ["merge", "--end-of-options", "refs/heads/other"], GitAccess.Write);
        Assert.False(merge.Success);

        var status = StatusParser.Parse((await repo.Git.RunAsync(repo.Path, StatusParser.Args)).Stdout);
        var entry = Assert.Single(status.Entries);
        Assert.Equal(EntryKind.Unmerged, entry.Kind);
        Assert.Equal(('U', 'U'), (entry.IndexState, entry.WorktreeState));
        Assert.Equal(FileStatus.Conflicted, entry.Unstaged!.Status);
        Assert.Null(entry.Staged);
    }

    [Fact]
    public async Task Status_reports_ahead_and_behind_despite_config()
    {
        using var remote = TempRepo.Init();
        remote.CommitFile("a.txt", "1\n", "one");
        using var clone = TempRepo.CloneOf(remote);
        clone.CommitFile("b.txt", "2\n", "local");
        remote.CommitFile("c.txt", "3\n", "remote");
        clone.Raw("fetch", "-q");

        var status = StatusParser.Parse((await clone.Git.RunCheckedAsync("status", clone.Path, StatusParser.Args)).Stdout);
        Assert.Equal((1, 1), (status.Branch.Ahead, status.Branch.Behind));
        Assert.Equal($"origin/{remote.CurrentBranch()}", status.Branch.Upstream);
    }

    [Fact]
    public async Task Diff_output_shape_is_independent_of_user_config_and_locale()
    {
        using var repo = TempRepo.Init();
        repo.CommitFile("a.txt", "one\n", "init");
        repo.Write("a.txt", "two\n");

        var diff = Encoding.UTF8.GetString((await repo.Git.RunCheckedAsync("diff", repo.Path, ["diff", "--no-ext-diff"])).Stdout);
        Assert.StartsWith("diff --git a/a.txt b/a.txt\n", diff);
        Assert.DoesNotContain("\u001b[", diff);
    }

    [Fact]
    public async Task Hostile_profile_really_changes_raw_git_output()
    {
        if (!TestEnvironment.Has("color.diff=always")) return;
        using var repo = TempRepo.Init();
        repo.CommitFile("a.txt", "one\n", "init");
        repo.Write("a.txt", "two\n");
        // Raw() pins color.ui only; color.diff=always must still win there,
        // and the runner must override it.
        Assert.Contains("\u001b[", repo.Raw("diff", "--no-ext-diff"));
        var hardened = await repo.Git.RunCheckedAsync("diff", repo.Path, ["diff", "--no-ext-diff"]);
        Assert.DoesNotContain("\u001b[", hardened.StdoutText);
    }

    [Fact]
    public async Task Branch_named_like_a_file_is_not_ambiguous()
    {
        // The old app passed bare branch names; a branch named like a path failed.
        using var repo = TempRepo.Init();
        repo.CommitFile("feature", "file named like the branch\n", "init");
        repo.Raw("branch", "feature");

        var log = await repo.Git.RunAsync(repo.Path, ["log", "--format=%H", "--end-of-options", "refs/heads/feature", "--"]);
        Assert.True(log.Success, log.StderrText);
    }

    [Fact]
    public async Task Pathspecs_are_literal()
    {
        if (OperatingSystem.IsWindows()) return;
        using var repo = TempRepo.Init();
        repo.CommitFile("keep.txt", "1\n", "init");
        repo.Write("glob*.txt", "x\n");
        repo.Write("other.txt", "y\n");

        await repo.Git.RunCheckedAsync("add", repo.Path, ["add", "--pathspec-from-file=-", "--pathspec-file-nul"], GitAccess.Write, GitPath.ToNulSeparated(["glob*.txt"]));

        var staged = repo.Raw("diff", "--cached", "--name-only").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(["glob*.txt"], staged);
    }

    [Fact]
    public async Task Failure_is_reported_by_exit_code_with_stderr_for_display()
    {
        using var repo = TempRepo.Init();
        var result = await repo.Git.RunAsync(repo.Path, ["rev-parse", "--verify", "--end-of-options", "refs/heads/nope"]);
        Assert.False(result.Success);
        var e = Assert.Throws<GitCommandException>(() => result.EnsureSuccess("Resolve"));
        Assert.Equal("Resolve", e.Operation);
    }

    [Fact]
    public async Task Invocations_are_logged_with_operation_and_directory()
    {
        using var repo = TempRepo.Init();
        var log = new List<GitInvocation>();
        repo.Git.Invoked += log.Add;
        using (GitRunner.BeginOperation("Refresh"))
        {
            await repo.Git.RunAsync(repo.Path, ["status", "--porcelain=v2"]);
        }
        await repo.Git.RunAsync(repo.Path, ["rev-parse", "--git-dir"]);

        Assert.Equal(2, log.Count);
        Assert.Equal("Refresh", log[0].Operation?.Label);
        Assert.Equal(repo.Path, log[0].WorkingDirectory);
        Assert.Null(log[1].Operation);
    }

    [Fact]
    public async Task Operations_with_the_same_label_stay_apart_and_nesting_restores_the_outer_one()
    {
        using var repo = TempRepo.Init();
        var log = new List<GitInvocation>();
        repo.Git.Invoked += log.Add;
        using (GitRunner.BeginOperation("Stage"))
        {
            await repo.Git.RunAsync(repo.Path, ["rev-parse", "--git-dir"]);
            using (GitRunner.BeginOperation("Refresh", background: true))
            {
                await repo.Git.RunAsync(repo.Path, ["status", "--porcelain=v2"]);
            }
            await repo.Git.RunAsync(repo.Path, ["rev-parse", "--git-dir"]);
        }
        using (GitRunner.BeginOperation("Stage"))
        {
            await repo.Git.RunAsync(repo.Path, ["rev-parse", "--git-dir"]);
        }

        Assert.Equal(4, log.Count);
        Assert.Same(log[0].Operation, log[2].Operation);
        Assert.True(log[1].Operation!.Background);
        Assert.False(log[0].Operation!.Background);
        Assert.NotEqual(log[0].Operation!.Id, log[3].Operation!.Id);
        Assert.Equal(log[0].Operation!.Label, log[3].Operation!.Label);
    }

    [Fact]
    public async Task Missing_git_executable_is_a_clear_error()
    {
        var runner = new GitRunner("/nonexistent/git-binary");
        await Assert.ThrowsAsync<GitNotFoundException>(() => runner.RunAsync(Path.GetTempPath(), ["version"]));
    }

    [Fact]
    public async Task Cancellation_kills_the_process()
    {
        if (OperatingSystem.IsWindows()) return;
        using var repo = TempRepo.Init();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var sw = System.Diagnostics.Stopwatch.StartNew();
        // A shell alias that would run for 30 s unless the process tree is killed.
        var task = repo.Git.StreamRecordsAsync(repo.Path, ["-c", "alias.slow=!sleep 30", "slow"], (byte)'\n', _ => { }, ct: cts.Token);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Cat_file_reads_commits_and_blobs_and_survives_misses()
    {
        using var repo = TempRepo.Init();
        repo.CommitFile("a.txt", "hello\n", "first\n\nbody line");
        using var cat = new CatFileProcess(repo.Git, repo.Path);

        var commit = cat.Read(Oid.ParseHex(repo.Head()));
        Assert.NotNull(commit);
        Assert.Equal("commit", commit.Type);
        var info = CommitParser.Parse(commit.Oid, commit.Content);
        Assert.Equal("first", info.Summary);
        Assert.Equal("body line", info.Body);
        Assert.Equal("Test User", info.Author.Name);

        Assert.Null(cat.Read("HEAD:missing.txt"));
        var blob = cat.Read("HEAD:a.txt");
        Assert.Equal("hello\n", Encoding.UTF8.GetString(blob!.Content).Replace("\r\n", "\n"));
    }

    [Fact]
    public async Task Version_is_detected_and_meets_the_minimum()
    {
        var version = await GitVersion.DetectAsync(new GitRunner());
        Assert.True(version.CompareTo(GitVersion.Minimum) >= 0, $"git {version} is older than {GitVersion.Minimum}");
    }

    [Theory]
    [InlineData("git version 2.34.1", 2, 34, 1)]
    [InlineData("git version 2.39.3 (Apple Git-146)", 2, 39, 3)]
    [InlineData("git version 2.45.1.windows.1", 2, 45, 1)]
    [InlineData("git version 2.50.0.rc1\n", 2, 50, 0)]
    public void Version_strings_parse(string text, int major, int minor, int patch)
    {
        var v = GitVersion.TryParse(text);
        Assert.Equal((major, minor, patch), (v!.Major, v.Minor, v.Patch));
    }

    [Theory]
    [InlineData("\"a\\tb\"", "a\tb")]
    [InlineData("\"q\\\"uote\"", "q\"uote")]
    [InlineData("\"\\303\\274\"", "ü")]
    [InlineData("\"back\\\\slash\" rest", "back\\slash")]
    public void Quoted_paths_unquote_to_exact_bytes(string quoted, string expected)
    {
        Assert.True(GitQuoting.TryUnquote(Encoding.UTF8.GetBytes(quoted), out var value, out _));
        Assert.Equal(expected, Encoding.UTF8.GetString(value));
    }
}

[CollectionDefinition(nameof(ProcessEnvironment), DisableParallelization = true)]
public class ProcessEnvironment;

/// <summary>
/// Tests that must change the process environment. Every git child inherits
/// it, so they run alone, after the parallel tests.
/// </summary>
[Collection(nameof(ProcessEnvironment))]
public class InheritedEnvironmentTests
{
    [Fact]
    public async Task Inherited_git_dir_variables_are_ignored()
    {
        using var repo = TempRepo.Init();
        repo.CommitFile("a.txt", "1\n", "init");
        using var other = TempRepo.Init();

        var runner = new GitRunner();
        // As if CheeseGit had been started from a hook running in another repository.
        Environment.SetEnvironmentVariable("GIT_DIR", Path.Combine(other.Path, ".git"));
        try
        {
            var result = await runner.RunCheckedAsync("rev-parse", repo.Path, ["rev-parse", "--show-toplevel"]);
            Assert.Equal(Path.GetFullPath(repo.Path), Path.GetFullPath(result.StdoutText.Trim()));
        }
        finally
        {
            Environment.SetEnvironmentVariable("GIT_DIR", null);
        }
    }
}
