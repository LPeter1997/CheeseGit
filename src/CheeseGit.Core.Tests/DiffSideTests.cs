using CheeseGit.Core.Diff;
using CheeseGit.Core.Repository;

namespace CheeseGit.Core.Tests;

/// <summary>Reading a diff's old and new version back as bytes (for non-text previews such as images).</summary>
public class DiffSideTests
{
    private static readonly byte[] V1 = [0x89, (byte)'P', 0, 1, 2, 3];
    private static readonly byte[] V2 = [0x89, (byte)'P', 0, 9, 9, 9, 9];

    private static async Task<(FileContent? Old, FileContent? New)> SidesAsync(GitRepository git, FileDiff diff, bool fromDisk, long max = 1 << 20) =>
        (await git.ReadDiffSideAsync(diff, DiffSide.Old, fromDisk, max), await git.ReadDiffSideAsync(diff, DiffSide.New, fromDisk, max));

    [Fact]
    public async Task Every_area_names_both_blobs_and_reads_them_back()
    {
        using var repo = TempRepo.Init();
        repo.Write("a.bin", V1);
        repo.CommitAll("v1");
        repo.Write("a.bin", V2);
        repo.Raw("add", "a.bin");
        repo.Write("a.bin", [.. V2, 7]);
        using var git = await repo.OpenAsync();

        var staged = await git.DiffFileAsync("a.bin", DiffArea.Staged);
        Assert.True(staged.IsBinary);
        Assert.NotNull(staged.OldBlob);
        var (old, @new) = await SidesAsync(git, staged, fromDisk: false);
        Assert.Equal(V1, old!.Bytes);
        Assert.Equal(V2, @new!.Bytes);

        var unstaged = await git.DiffFileAsync("a.bin", DiffArea.Unstaged);
        Assert.Equal(staged.NewBlob, unstaged.OldBlob);
        Assert.NotNull(unstaged.NewBlob); // git hashes the working tree file
        (old, @new) = await SidesAsync(git, unstaged, fromDisk: true);
        Assert.Equal(V2, old!.Bytes);
        Assert.Equal([.. V2, 7], @new!.Bytes);

        repo.Raw("commit", "-qm", "v2"); // the index (V2), not the working tree
        var commit = await git.DiffCommitFileAsync(Git.Oid.ParseHex(repo.Head()), "a.bin");
        (old, @new) = await SidesAsync(git, commit, fromDisk: false);
        Assert.Equal(V1, old!.Bytes);
        Assert.Equal(V2, @new!.Bytes);
    }

    [Fact]
    public async Task Added_untracked_and_deleted_files_have_one_side()
    {
        using var repo = TempRepo.Init();
        repo.Write("gone.bin", V1);
        repo.CommitAll("v1");
        repo.Delete("gone.bin");
        repo.Write("new.bin", V2);
        using var git = await repo.OpenAsync();

        var untracked = await git.DiffFileAsync("new.bin", DiffArea.Unstaged);
        Assert.Equal(DiffChange.Added, untracked.Change);
        Assert.Null(untracked.OldBlob);
        var (old, @new) = await SidesAsync(git, untracked, fromDisk: true);
        Assert.Null(old);
        Assert.Equal(V2, @new!.Bytes);

        var deleted = await git.DiffFileAsync("gone.bin", DiffArea.Unstaged);
        Assert.Null(deleted.NewBlob);
        (old, @new) = await SidesAsync(git, deleted, fromDisk: true);
        Assert.Equal(V1, old!.Bytes);
        Assert.Null(@new);
    }

    [Fact]
    public async Task A_version_over_the_limit_has_a_size_but_no_bytes_and_the_reader_stays_in_sync()
    {
        using var repo = TempRepo.Init();
        var big = new byte[200_000];
        big[0] = 0;
        Random.Shared.NextBytes(big.AsSpan(1));
        repo.Write("big.bin", big);
        repo.CommitAll("big");
        repo.Write("big.bin", V1);
        using var git = await repo.OpenAsync();

        var diff = await git.DiffFileAsync("big.bin", DiffArea.Unstaged);
        var (old, @new) = await SidesAsync(git, diff, fromDisk: true, max: 1000);
        Assert.Equal(200_000, old!.Size);
        Assert.Null(old.Bytes);
        Assert.Equal(V1, @new!.Bytes);
        // The skipped content was drained: the next read through the same cat-file process is intact.
        Assert.Equal(big, (await git.ReadDiffSideAsync(diff, DiffSide.Old, true, 1 << 20))!.Bytes);
    }
}
