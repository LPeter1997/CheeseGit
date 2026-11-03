using System.Diagnostics;
using SHA256 = System.Security.Cryptography.SHA256;
using System.Text;
using CheeseGit.Core.Diff;
using CheeseGit.Core.Git;
using CheeseGit.Core.Repository;

namespace CheeseGit.Core.Tests;

/// <summary>
/// Git LFS: pointers resolve to their real content, LFS files are marked, their lines cannot be
/// staged, missing objects download, and setup and tracking. Tests that need git-lfs return early
/// without it (xunit 2 has no runtime skip); CI images have it.
/// </summary>
public class LfsTests
{
    private static readonly Lazy<bool> LfsInstalled = new(() =>
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("git", ["lfs", "version"]) { RedirectStandardOutput = true, RedirectStandardError = true })!;
            p.WaitForExit();
            return p.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    });

    private static readonly byte[] V1 = [0x89, (byte)'P', (byte)'N', (byte)'G', 1, 1, 1, 0, 1];
    private static readonly byte[] V2 = [0x89, (byte)'P', (byte)'N', (byte)'G', 2, 2, 0, 2, 2, 2];

    private static string Sha256(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static byte[] PointerBytes(byte[] content) => LfsPointer.For(Sha256(content), content.Length).Raw;

    /// <summary>A repository with git-lfs configured in its own config (the test home has none) and *.bin tracked.</summary>
    private static TempRepo LfsRepo()
    {
        var repo = TempRepo.Init();
        repo.Raw("lfs", "install", "--local");
        repo.Write(".gitattributes", "*.bin filter=lfs diff=lfs merge=lfs -text\n");
        repo.CommitAll("attributes");
        return repo;
    }

    [Fact]
    public void A_pointer_parses_and_anything_else_does_not()
    {
        var oid = new string('a', 64);
        var pointer = LfsPointer.TryParse(Encoding.ASCII.GetBytes($"version https://git-lfs.github.com/spec/v1\noid sha256:{oid}\nsize 12345\n"));
        Assert.NotNull(pointer);
        Assert.Equal(oid, pointer.Oid);
        Assert.Equal(12345, pointer.Size);
        Assert.Equal("aaaaaaaaaa", pointer.ShortOid);

        // Extension lines sit between the version and the oid.
        Assert.NotNull(LfsPointer.TryParse(Encoding.ASCII.GetBytes($"version https://git-lfs.github.com/spec/v1\next-0-foo sha256:{new string('b', 64)}\noid sha256:{oid}\nsize 1\n")));
        Assert.Equal(LfsPointer.For(oid, 7).Raw, LfsPointer.TryParse(LfsPointer.For(oid, 7).Raw)!.Raw);

        Assert.Null(LfsPointer.TryParse([]));
        Assert.Null(LfsPointer.TryParse(Encoding.ASCII.GetBytes($"version https://git-lfs.github.com/spec/v1\noid sha256:{oid}\nsize 1"))); // no final newline
        Assert.Null(LfsPointer.TryParse(Encoding.ASCII.GetBytes($"version https://example.com/spec\noid sha256:{oid}\nsize 1\n")));
        Assert.Null(LfsPointer.TryParse(Encoding.ASCII.GetBytes($"version https://git-lfs.github.com/spec/v1\noid sha256:{oid.ToUpperInvariant()}\nsize 1\n")));
        Assert.Null(LfsPointer.TryParse(Encoding.ASCII.GetBytes($"version https://git-lfs.github.com/spec/v1\noid sha256:{oid}\nsize -1\n")));
        Assert.Null(LfsPointer.TryParse(Encoding.ASCII.GetBytes($"version https://git-lfs.github.com/spec/v1\nsize 1\n"))); // no oid
        Assert.Null(LfsPointer.TryParse(Encoding.ASCII.GetBytes($"oid sha256:{oid}\nversion https://git-lfs.github.com/spec/v1\nsize 1\n"))); // version not first
        Assert.Null(LfsPointer.TryParse([.. LfsPointer.For(oid, 1).Raw, .. new byte[LfsPointer.MaxSize]]));
    }

    [Fact]
    public async Task A_pointer_diff_is_recognised_and_its_lines_cannot_be_staged()
    {
        // Pointers committed without git-lfs: what a diff of an LFS file looks like to git.
        using var repo = TempRepo.Init();
        repo.Write("art.bin", PointerBytes(V1));
        repo.Write("notes.txt", "version 1\noid of the thing\n");
        repo.CommitAll("v1");
        repo.Write("art.bin", PointerBytes(V2));
        repo.Write("notes.txt", "version 2\noid of the thing\n");
        using var git = await repo.OpenAsync();

        var diff = await git.DiffFileAsync("art.bin", DiffArea.Unstaged);
        Assert.False(diff.IsBinary);
        Assert.True(LfsPointer.IsPointerDiff(diff));
        Assert.False(LfsPointer.IsPointerDiff(await git.DiffFileAsync("notes.txt", DiffArea.Unstaged)));

        var oidLine = diff.Hunks[0].Lines.Select((l, i) => (l, i)).First(x => x.l.Kind == DiffLineKind.Addition).i;
        var error = await Assert.ThrowsAsync<GitOperationException>(() => git.StageLinesAsync(diff, [new LineSelection(0, oidLine)]));
        Assert.Equal(GitErrorCode.Unsupported, error.Code);
        await Assert.ThrowsAsync<GitOperationException>(() => git.DiscardLinesAsync(diff, [new LineSelection(0, oidLine)], DiffArea.Unstaged));
        Assert.Equal(PointerBytes(V1), repo.RawBytes("cat-file", "blob", ":art.bin"));

        // With diff.context=0 the unchanged version line is not in the diff.
        repo.Raw("config", "diff.context", "0");
        Assert.True(LfsPointer.IsPointerDiff(await git.DiffFileAsync("art.bin", DiffArea.Unstaged)));
    }

    [Fact]
    public async Task A_side_that_is_a_pointer_without_its_object_comes_back_missing_with_the_pointers_size()
    {
        using var repo = TempRepo.Init();
        repo.Write("art.bin", PointerBytes(V1));
        repo.CommitAll("v1");
        repo.Write("art.bin", PointerBytes(V2));
        using var git = await repo.OpenAsync();

        var diff = await git.DiffFileAsync("art.bin", DiffArea.Unstaged);
        var old = await git.ReadDiffSideAsync(diff, DiffSide.Old, newFromWorkingTree: true, maxBytes: 0);
        var @new = await git.ReadDiffSideAsync(diff, DiffSide.New, newFromWorkingTree: true, maxBytes: 1 << 20);
        Assert.True(old!.IsMissing);
        Assert.Equal(V1.Length, old.Size);
        Assert.Equal(Sha256(V1), old.Lfs!.Oid);
        Assert.True(@new!.IsMissing); // a pointer on disk: git-lfs never checked it out
        Assert.Equal(Sha256(V2), @new.Lfs!.Oid);
    }

    [Fact]
    public async Task Every_area_reads_the_real_content_from_the_local_store()
    {
        if (!LfsInstalled.Value) return;
        using var repo = LfsRepo();
        repo.Write("art.bin", V1);
        repo.CommitAll("v1");
        repo.Write("art.bin", V2);
        using var git = await repo.OpenAsync();

        // git diffs the pointers (the clean filter runs on the working tree file).
        var unstaged = await git.DiffFileAsync("art.bin", DiffArea.Unstaged);
        Assert.True(LfsPointer.IsPointerDiff(unstaged));
        var old = await git.ReadDiffSideAsync(unstaged, DiffSide.Old, true, 1 << 20);
        var @new = await git.ReadDiffSideAsync(unstaged, DiffSide.New, true, 1 << 20);
        Assert.Equal(V1, old!.Bytes);
        Assert.NotNull(old.Lfs);
        Assert.Equal(V2, @new!.Bytes); // the checked-out file itself
        Assert.Null(@new.Lfs);

        repo.Raw("add", "art.bin");
        var staged = await git.DiffFileAsync("art.bin", DiffArea.Staged);
        Assert.True(LfsPointer.IsPointerDiff(staged));
        old = await git.ReadDiffSideAsync(staged, DiffSide.Old, false, 1 << 20);
        @new = await git.ReadDiffSideAsync(staged, DiffSide.New, false, 1 << 20);
        Assert.Equal(V1, old!.Bytes);
        Assert.Equal(V2, @new!.Bytes);
        Assert.Equal(Sha256(V2), @new.Lfs!.Oid);

        // Over the caller's limit: the size of the real content, no bytes.
        @new = await git.ReadDiffSideAsync(staged, DiffSide.New, false, 4);
        Assert.Null(@new!.Bytes);
        Assert.False(@new.IsMissing);
        Assert.Equal(V2.Length, @new.Size);

        repo.Raw("commit", "-qm", "v2");
        var commit = await git.DiffCommitFileAsync(Oid.ParseHex(repo.Head()), "art.bin");
        Assert.Equal(V1, (await git.ReadDiffSideAsync(commit, DiffSide.Old, false, 1 << 20))!.Bytes);
        Assert.Equal(V2, (await git.ReadDiffSideAsync(commit, DiffSide.New, false, 1 << 20))!.Bytes);
    }

    [Fact]
    public async Task Status_and_commit_files_mark_lfs_files()
    {
        if (!LfsInstalled.Value) return;
        using var repo = LfsRepo();
        repo.Write("art.bin", V1);
        repo.Write("readme.txt", "hi\n");
        repo.CommitAll("files");
        repo.Write("art.bin", V2);
        repo.Write("readme.txt", "hello\n");
        repo.Write("new.bin", V1);
        using var git = await repo.OpenAsync();

        var status = await git.StatusAsync();
        Assert.True(status.Entries.Single(e => e.Path == "art.bin").IsLfs);
        Assert.True(status.Entries.Single(e => e.Path == "new.bin").IsLfs);
        Assert.False(status.Entries.Single(e => e.Path == "readme.txt").IsLfs);
        Assert.True(status.Unstaged.Single(c => c.Path == "art.bin").IsLfs);

        var files = await git.ListCommitFilesAsync(Oid.ParseHex(repo.Head()));
        Assert.True(files.Single(f => f.Path == "art.bin").IsLfs);
        Assert.False(files.Single(f => f.Path == "readme.txt").IsLfs);

        // From git 2.40 a commit's files are judged by that commit's attributes: here the pattern is gone since.
        var tracked = Oid.ParseHex(repo.Head());
        repo.Write(".gitattributes", "");
        repo.CommitAll("no lfs");
        Assert.Equal(git.Version.CompareTo(new GitVersion(2, 40, 0, "2.40.0")) >= 0, (await git.ListCommitFilesAsync(tracked)).Single(f => f.Path == "art.bin").IsLfs);
    }

    [Fact]
    public async Task Without_lfs_attributes_nothing_is_marked()
    {
        using var repo = TempRepo.Init();
        repo.Write("a.bin", V1);
        using var git = await repo.OpenAsync();
        Assert.False((await git.StatusAsync()).Entries.Single().IsLfs);
        Assert.Equal(LfsSetup.NotUsed, await git.LfsSetupAsync());
    }

    [Fact]
    public async Task A_missing_object_downloads_from_the_remote_into_the_local_store()
    {
        if (!LfsInstalled.Value) return;
        using var origin = TempRepo.InitBare();
        using var repo = LfsRepo();
        repo.Write("art.bin", V1);
        repo.CommitAll("v1");
        repo.Raw("remote", "add", "origin", origin.Path);
        repo.Raw("push", "-q", "origin", "HEAD:refs/heads/main"); // the pre-push hook uploads to the file:// LFS endpoint

        using var clone = TempRepo.EmptyDirectory();
        clone.RawWithEnv(new Dictionary<string, string> { ["GIT_LFS_SKIP_SMUDGE"] = "1" }, "clone", "-q", "-b", "main", origin.Path, clone.Path);
        clone.Raw("lfs", "install", "--local");
        using var git = await clone.OpenAsync();
        var commit = await git.DiffCommitFileAsync(Oid.ParseHex(clone.Head()), "art.bin");
        var side = await git.ReadDiffSideAsync(commit, DiffSide.New, false, 1 << 20);
        Assert.True(side!.IsMissing);

        var downloaded = await git.DownloadLfsObjectAsync(side.Lfs!, 1 << 20);
        Assert.Equal(V1, downloaded.Bytes);
        var again = await git.ReadDiffSideAsync(commit, DiffSide.New, false, 1 << 20);
        Assert.False(again!.IsMissing);
        Assert.Equal(V1, again.Bytes);
    }

    [Fact]
    public async Task A_download_without_a_remote_fails()
    {
        if (!LfsInstalled.Value) return;
        using var repo = LfsRepo();
        using var git = await repo.OpenAsync();
        await Assert.ThrowsAnyAsync<GitException>(() => git.DownloadLfsObjectAsync(LfsPointer.For(Sha256(V2), V2.Length), 1 << 20));
    }

    [Fact]
    public async Task Setup_reports_a_missing_filter_and_hook_and_fixes_them()
    {
        if (!LfsInstalled.Value) return;
        using var repo = TempRepo.Init();
        repo.Write(".gitattributes", "*.bin filter=lfs diff=lfs merge=lfs -text\n");
        repo.CommitAll("attributes");
        using var git = await repo.OpenAsync();
        Assert.Equal(LfsSetup.NotConfigured, await git.LfsSetupAsync()); // the test home has no filter

        repo.Raw("lfs", "install", "--local");
        Assert.Equal(LfsSetup.Ready, await git.LfsSetupAsync());

        File.Delete(Path.Combine(repo.Path, ".git", "hooks", "pre-push"));
        Assert.Equal(LfsSetup.NotConfigured, await git.LfsSetupAsync());
        // The filter is there (in the repository's config), so only the hooks are installed:
        // nothing is written to the user config, which the tests share.
        await git.SetUpLfsAsync();
        Assert.DoesNotContain("lfs", File.ReadAllText(TestEnvironment.GlobalConfigPath));
        Assert.Equal(LfsSetup.Ready, await git.LfsSetupAsync());
    }

    [Fact]
    public async Task Attributes_in_a_subfolder_count_as_using_lfs()
    {
        if (!LfsInstalled.Value) return;
        using var repo = TempRepo.Init();
        repo.Write("assets/.gitattributes", "*.bin filter=lfs diff=lfs merge=lfs -text\n");
        repo.CommitAll("attributes");
        using var git = await repo.OpenAsync();
        Assert.NotEqual(LfsSetup.NotUsed, await git.LfsSetupAsync());
    }

    [Fact]
    public async Task Tracking_turns_a_staged_file_into_a_pointer_and_stages_the_attributes()
    {
        if (!LfsInstalled.Value) return;
        using var repo = TempRepo.WithCommit();
        repo.Raw("lfs", "install", "--local");
        repo.Write("sound.wav", V1);
        repo.Raw("add", "sound.wav");
        Assert.Equal(V1, repo.RawBytes("cat-file", "blob", ":sound.wav"));
        using var git = await repo.OpenAsync();

        await git.TrackWithLfsAsync("*.wav", ["sound.wav"]);
        Assert.Equal(PointerBytes(V1), repo.RawBytes("cat-file", "blob", ":sound.wav"));
        Assert.Contains("*.wav filter=lfs", Encoding.UTF8.GetString(repo.RawBytes("cat-file", "blob", ":.gitattributes")));
        Assert.Equal(V1, File.ReadAllBytes(repo.FullPath("sound.wav")));
        await Assert.ThrowsAsync<GitOperationException>(() => git.TrackWithLfsAsync("--help", []));
    }

    /// <summary>main and feature both change art.bin (an LFS file) and big.txt / blob.dat (not LFS); merging feature conflicts.</summary>
    private static TempRepo ConflictingBranches()
    {
        var repo = LfsRepo();
        var text = string.Concat(Enumerable.Range(0, 2000).Select(i => $"line {i}\n")); // over 8000 bytes, still text
        repo.Write("art.bin", V1);
        repo.Write("big.txt", text);
        repo.Write("blob.dat", [0, 1, 2]);
        repo.CommitAll("base");
        repo.Raw("switch", "-q", "-c", "feature");
        repo.Write("art.bin", V2);
        repo.Write("big.txt", "feature\n" + text);
        repo.Write("blob.dat", [0, 9, 9]);
        repo.CommitAll("feature");
        repo.Raw("switch", "-q", "-");
        repo.Write("art.bin", [.. V1, 7]);
        repo.Write("big.txt", "main\n" + text);
        repo.Write("blob.dat", [0, 5, 5]);
        repo.CommitAll("main");
        Assert.NotEqual(0, repo.TryRaw("merge", "feature").ExitCode);
        return repo;
    }

    [Fact]
    public async Task A_conflicted_lfs_file_is_told_apart_and_resolves_to_a_whole_side()
    {
        if (!LfsInstalled.Value) return;
        using var repo = ConflictingBranches();
        using var git = await repo.OpenAsync();

        var infos = (await git.ConflictCountsAsync()).ToDictionary(i => i.Path.ToString());
        Assert.Equal((true, false, 0), (infos["art.bin"].IsLfs, infos["art.bin"].IsBinary, infos["art.bin"].ConflictCount)); // git merged the pointers as text
        Assert.Equal((false, true), (infos["blob.dat"].IsLfs, infos["blob.dat"].IsBinary));
        Assert.Equal((false, false, 1), (infos["big.txt"].IsLfs, infos["big.txt"].IsBinary, infos["big.txt"].ConflictCount));
        Assert.True(infos["big.txt"].CanMergeLines);
        Assert.False(infos["art.bin"].CanMergeLines);

        var error = await Assert.ThrowsAsync<GitOperationException>(() => git.ResolveConflictAsync("art.bin", ConflictResolution.AcceptBoth));
        Assert.Equal(GitErrorCode.Unsupported, error.Code);

        await git.ResolveConflictAsync("art.bin", ConflictResolution.AcceptIncoming);
        Assert.Equal(V2, File.ReadAllBytes(repo.FullPath("art.bin"))); // checked out through git-lfs
        Assert.Equal(PointerBytes(V2), repo.RawBytes("cat-file", "blob", ":art.bin"));
    }

    [Fact]
    public async Task A_clone_checks_out_the_lfs_files_themselves()
    {
        if (!LfsInstalled.Value) return;
        using var origin = TempRepo.InitBare();
        using (var work = LfsRepo())
        {
            work.Write("art.bin", V1);
            work.CommitAll("art");
            work.Raw("remote", "add", "origin", origin.Path);
            work.Raw("push", "-q", "origin", "HEAD:refs/heads/main");
        }
        origin.Raw("symbolic-ref", "HEAD", "refs/heads/main");
        using var parent = TempRepo.EmptyDirectory();
        // The filter as git lfs install writes it, for this runner only: the tests share their user config.
        var runner = new GitRunner();
        string[][] filter = [["filter.lfs.process", "git-lfs filter-process"], ["filter.lfs.smudge", "git-lfs smudge -- %f"], ["filter.lfs.clean", "git-lfs clean -- %f"], ["filter.lfs.required", "true"]];
        runner.Environment["GIT_CONFIG_COUNT"] = filter.Length.ToString();
        for (var i = 0; i < filter.Length; i++)
        {
            runner.Environment[$"GIT_CONFIG_KEY_{i}"] = filter[i][0];
            runner.Environment[$"GIT_CONFIG_VALUE_{i}"] = filter[i][1];
        }

        using var clone = await GitRepository.CloneAsync(origin.Path, parent.Path, runner);
        Assert.Equal(V1, File.ReadAllBytes(Path.Combine(clone.Root, "art.bin")));
        Assert.True((await clone.StatusAsync()).Entries.Count == 0);
    }
}
