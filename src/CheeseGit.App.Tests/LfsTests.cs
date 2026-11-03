using System.Diagnostics;
using System.Text;
using Avalonia.Headless.XUnit;
using CheeseGit.App.Services;
using CheeseGit.App.ViewModels;
using CheeseGit.Core.Git;
using CheeseGit.Core.Repository;
using CheeseGit.Core.Tests;

namespace CheeseGit.App.Tests;

/// <summary>
/// Git LFS files show as the files they stand for, are tagged in the lists, cannot be staged by
/// line, download when missing; the banner and the track offer. Without git-lfs these return early.
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

    private static TempRepo LfsRepo()
    {
        var repo = TempRepo.Init();
        repo.Raw("lfs", "install", "--local");
        repo.Write(".gitattributes", "*.png filter=lfs diff=lfs merge=lfs -text\n*.psd filter=lfs diff=lfs merge=lfs -text\n");
        repo.CommitAll("attributes");
        return repo;
    }

    [AvaloniaFact]
    public async Task A_modified_lfs_image_shows_both_pictures_and_its_lines_cannot_be_staged()
    {
        if (!LfsInstalled.Value) return;
        using var repo = LfsRepo();
        repo.Write("art/hero.png", ImageDiffTests.Png(16, 16));
        repo.CommitAll("hero");
        repo.Write("art/hero.png", ImageDiffTests.Png(32, 16, 0xFFCC3333));
        using var app = await AppHarness.OpenAsync(repo);

        var row = app.Unstaged("art/hero.png");
        Assert.True(row.IsLfs);
        await app.ShowAsync(row);
        await AppHarness.Until(() => app.Diff.Images is not null, "the LFS image versions");
        var images = app.Diff.Images!;
        Assert.True(images.Old!.HasBitmap);
        Assert.True(images.New!.HasBitmap);
        Assert.Equal((16, 32), (images.Old.PixelWidth, images.New.PixelWidth));
        Assert.True(app.Diff.IsLfs);
        Assert.False(app.Diff.CanEditLines);
        Assert.False(app.Diff.ShowDiff);
        Assert.False(app.Diff.CanSearch); // no search or unified/split for pictures
        Assert.False(app.Diff.HasLfsTrackOffer);
        await AppHarness.Until(() => app.Staging.Unstaged.TotalAdditions == "" && row.Stats is null, "the stats, without the pointer's lines");

        // Staged whole, it is still shown as the pictures (both versions from the local store).
        await app.Staging.Unstaged.RowActionCommand.ExecuteAsync(row);
        await AppHarness.Until(() => app.StagedPaths.Contains("art/hero.png"), "the file to be staged");
        await app.ShowAsync(app.StagedRow("art/hero.png"));
        await AppHarness.Until(() => app.Diff.Images is { New.HasBitmap: true } && app.Diff.Area == DiffArea.Staged, "the staged LFS image");
        Assert.True(app.StagedRow("art/hero.png").IsLfs);
    }

    [AvaloniaFact]
    public async Task Another_lfs_file_shows_its_sizes_instead_of_the_pointer()
    {
        if (!LfsInstalled.Value) return;
        using var repo = LfsRepo();
        repo.Write("cover.psd", new byte[3000]);
        repo.CommitAll("psd");
        repo.Write("cover.psd", new byte[5000]);
        using var app = await AppHarness.OpenAsync(repo);

        await app.ShowAsync(app.Unstaged("cover.psd"));
        await AppHarness.Until(() => app.Diff.Message is not null, "the LFS summary");
        Assert.Equal("Stored in Git LFS (2.9 KB → 4.9 KB). There is no preview for this type of file.", app.Diff.Message);
        Assert.True(app.Diff.IsLfs);
        Assert.False(app.Diff.CanEditLines);
    }

    /// <summary>A clone whose LFS objects were never downloaded, with the history's image in a commit.</summary>
    private static (TempRepo Origin, TempRepo Clone, Oid Commit) CloneWithoutObjects(byte[] v1, byte[] v2)
    {
        var origin = TempRepo.InitBare();
        using var work = LfsRepo();
        work.Write("hero.png", v1);
        work.CommitAll("v1");
        work.Write("hero.png", v2);
        work.CommitAll("v2");
        work.Raw("remote", "add", "origin", origin.Path);
        work.Raw("push", "-q", "origin", "HEAD:refs/heads/main");
        var clone = TempRepo.EmptyDirectory();
        clone.RawWithEnv(new Dictionary<string, string> { ["GIT_LFS_SKIP_SMUDGE"] = "1" }, "clone", "-q", "-b", "main", origin.Path, clone.Path);
        clone.Raw("lfs", "install", "--local");
        return (origin, clone, Oid.ParseHex(clone.Head()));
    }

    [AvaloniaFact]
    public async Task Missing_versions_of_an_image_download_by_themselves_when_small()
    {
        if (!LfsInstalled.Value) return;
        var (origin, clone, commit) = CloneWithoutObjects(ImageDiffTests.Png(8, 8), ImageDiffTests.Png(12, 8));
        using var _o = origin;
        using var _c = clone;
        using var app = await AppHarness.OpenAsync(clone);

        app.Diff.ShowCommit(commit, "hero.png", "hero.png");
        await AppHarness.Until(() => app.Diff.Images is { Old.HasBitmap: true, New.HasBitmap: true }, "the downloaded versions");
        Assert.Equal(12, app.Diff.Images!.New!.PixelWidth);
        Assert.True(app.Diff.IsLfs);
    }

    [AvaloniaFact]
    public async Task With_downloads_off_a_missing_version_offers_the_download()
    {
        if (!LfsInstalled.Value) return;
        var (origin, clone, commit) = CloneWithoutObjects(ImageDiffTests.Png(8, 8), ImageDiffTests.Png(12, 8));
        using var _o = origin;
        using var _c = clone;
        using var app = await AppHarness.OpenAsync(clone, new AppSettings { LfsAutoDownloadMegabytes = 0 });

        app.Diff.ShowCommit(commit, "hero.png", "hero.png");
        await AppHarness.Until(() => app.Diff.Images is not null, "the image cards");
        var cards = app.Diff.Images!;
        Assert.True(cards.New!.CanDownload);
        Assert.False(cards.New.HasBitmap);
        Assert.Contains("not on this computer", cards.New.Problem);

        cards.DownloadCommand!.Execute(null);
        await AppHarness.Until(() => app.Diff.Images is { Old.HasBitmap: true, New.HasBitmap: true }, "the downloaded versions");
    }

    [AvaloniaFact]
    public async Task A_failed_download_says_why_and_is_not_retried_by_refreshes()
    {
        if (!LfsInstalled.Value) return;
        var (origin, clone, commit) = CloneWithoutObjects(ImageDiffTests.Png(8, 8), ImageDiffTests.Png(12, 8));
        using var _o = origin;
        using var _c = clone;
        // A file URL: git-lfs read a missing "C:\..." path as an scp-style ssh address (host "C") on Windows and hung in ssh.
        clone.Raw("remote", "set-url", "origin", new Uri(Path.Combine(origin.Path, "gone")).AbsoluteUri);
        using var app = await AppHarness.OpenAsync(clone);

        app.Diff.ShowCommit(commit, "hero.png", "hero.png");
        await AppHarness.Until(() => app.Diff.Images is { New.CanDownload: true }, "the failure");
        Assert.StartsWith("Could not download", app.Diff.Images!.New!.Problem);
        app.Diff.Reload();
        await Task.Delay(200);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.StartsWith("Could not download", app.Diff.Images!.New!.Problem);
    }

    [AvaloniaFact]
    public async Task Lfs_files_of_a_commit_are_tagged()
    {
        if (!LfsInstalled.Value) return;
        using var repo = LfsRepo();
        repo.Write("hero.png", ImageDiffTests.Png(8, 8));
        repo.Write("notes.txt", "notes\n");
        repo.CommitAll("files");
        using var app = await AppHarness.OpenAsync(repo);
        var files = await app.Tab.Session!.Repository.ListCommitFilesAsync(Oid.ParseHex(repo.Head()));
        Assert.Equal([("hero.png", true), ("notes.txt", false)], files.Select(f => (f.Path.ToString(), f.IsLfs)).Order());
    }

    [AvaloniaFact]
    public async Task The_banner_reports_missing_hooks_and_sets_them_up()
    {
        if (!LfsInstalled.Value) return;
        using var repo = LfsRepo();
        File.Delete(Path.Combine(repo.Path, ".git", "hooks", "pre-push"));
        using var app = await AppHarness.OpenAsync(repo);
        await AppHarness.Until(() => app.Tab.HasLfsBanner, "the LFS banner");
        Assert.True(app.Tab.LfsCanSetUp);
        Assert.Contains("not set up", app.Tab.LfsBanner);

        await app.Tab.SetUpLfsCommand.ExecuteAsync(null);
        Assert.Equal(LfsSetup.Ready, app.Tab.LfsSetup);
        Assert.False(app.Tab.HasLfsBanner);
    }

    [AvaloniaFact]
    public async Task A_repository_without_lfs_has_no_banner()
    {
        using var repo = TempRepo.WithCommit();
        using var app = await AppHarness.OpenAsync(repo);
        await Task.Delay(100);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(LfsSetup.NotUsed, app.Tab.LfsSetup);
        Assert.False(app.Tab.HasLfsBanner);
    }

    [AvaloniaFact]
    public async Task A_large_new_binary_file_offers_tracking_with_lfs()
    {
        if (!LfsInstalled.Value) return;
        using var repo = TempRepo.WithCommit();
        repo.Raw("lfs", "install", "--local");
        var big = new byte[(int)DiffViewModel.LfsTrackThreshold];
        big[0] = 0;
        Random.Shared.NextBytes(big.AsSpan(1));
        repo.Write("music.ogg", big);
        repo.Write("small.bin", new byte[] { 0, 1, 2 });
        repo.Raw("add", "music.ogg");
        using var app = await AppHarness.OpenAsync(repo);
        await AppHarness.Until(() => app.Tab.LfsInstalled, "the LFS check");

        await app.ShowAsync(app.Unstaged("small.bin"));
        await AppHarness.Until(() => app.Diff.Message is not null, "the small file");
        Assert.False(app.Diff.HasLfsTrackOffer);

        await app.ShowAsync(app.StagedRow("music.ogg"));
        await AppHarness.Until(() => app.Diff.HasLfsTrackOffer, "the offer");
        Assert.Equal("*.ogg", app.Diff.LfsTrackPattern);
        Assert.Equal("Track *.ogg with Git LFS", app.Diff.LfsTrackButtonText);
        Assert.StartsWith("A 1 MB binary file.", app.Diff.LfsTrackText);

        var tracking = app.Diff.TrackWithLfsCommand.ExecuteAsync(null);
        await AppHarness.Until(() => app.Main.Dialog is ConfirmDialogViewModel, "the question");
        ((ConfirmDialogViewModel)app.Main.Dialog!).ConfirmCommand.Execute(null);
        await tracking;
        Assert.NotNull(LfsPointer.TryParse(repo.RawBytes("cat-file", "blob", ":music.ogg")));
        Assert.Contains("*.ogg filter=lfs", Encoding.UTF8.GetString(repo.RawBytes("cat-file", "blob", ":.gitattributes")));
        await AppHarness.Until(() => app.StagedRow("music.ogg").IsLfs, "the LFS tag");
        // Now an LFS file: shown as one, no offer.
        await app.ShowAsync(app.StagedRow("music.ogg"));
        await AppHarness.Until(() => app.Diff.IsLfs, "the LFS diff");
        Assert.False(app.Diff.HasLfsTrackOffer);
    }

    [AvaloniaFact]
    public async Task A_conflicted_lfs_image_offers_current_and_incoming_only()
    {
        if (!LfsInstalled.Value) return;
        using var repo = LfsRepo();
        var (v1, v2, v3) = (ImageDiffTests.Png(8, 8), ImageDiffTests.Png(8, 8, 0xFF00FF00), ImageDiffTests.Png(8, 8, 0xFFFF0000));
        repo.Write("hero.png", v1);
        repo.Write("notes.txt", "notes\n");
        repo.CommitAll("base");
        repo.Raw("switch", "-q", "-c", "feature");
        repo.Write("hero.png", v2);
        repo.Write("notes.txt", "feature notes\n");
        repo.CommitAll("feature");
        repo.Raw("switch", "-q", "-");
        repo.Write("hero.png", v3);
        repo.Write("notes.txt", "main notes\n");
        repo.CommitAll("main");
        using var app = await AppHarness.OpenAsync(repo);

        await app.Tab.BranchList.LoadAsync();
        var merging = app.Tab.BranchList.Items.OfType<LocalBranchRow>().Single(r => r.Name == "feature").MergeCommand.ExecuteAsync(null);
        await AppHarness.Until(() => app.Main.Dialog is ConflictDialogViewModel { Loaded: true }, "the conflict dialog");
        var dialog = (ConflictDialogViewModel)app.Main.Dialog!;
        var image = dialog.Files.Single(f => f.DisplayPath == "hero.png");
        var notes = dialog.Files.Single(f => f.DisplayPath == "notes.txt");
        Assert.True(image.IsLfs);
        Assert.False(image.CanMergeLines);
        Assert.Equal("!", image.CountText); // no meaningful markers: a side has to be chosen
        Assert.True(notes.CanMergeLines);

        image.ChooseIncomingCommand.Execute(null);
        notes.ChooseCurrentCommand.Execute(null);
        await dialog.CompleteCommand.ExecuteAsync(null);
        await merging;
        Assert.Equal(v2, File.ReadAllBytes(repo.FullPath("hero.png")));
        Assert.Equal("main notes\n", repo.ReadNormalized("notes.txt"));
        Assert.Equal("", repo.Raw("status", "--porcelain").Trim());
    }

    [AvaloniaFact]
    public async Task Publish_pull_and_switch_carry_lfs_files_both_ways()
    {
        if (!LfsInstalled.Value) return;
        using var origin = TempRepo.InitBare();
        using var repo = LfsRepo();
        var (v1, v2) = (ImageDiffTests.Png(8, 8), ImageDiffTests.Png(16, 8));
        repo.Write("hero.png", v1);
        repo.CommitAll("v1");
        repo.Raw("branch", "first");
        repo.Raw("remote", "add", "origin", origin.Path);
        using var app = await AppHarness.OpenAsync(repo);
        var branch = repo.CurrentBranch();

        // Publish: the pre-push hook uploads the object to the remote's LFS store.
        var remote = app.Tab.Remote;
        await AppHarness.Until(() => remote.Action == RemoteAction.Publish, "the publish action");
        await remote.RunCommand.ExecuteAsync(null);
        Assert.Equal(repo.Head(), origin.Raw("rev-parse", branch).Trim());
        var oid = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(v1));
        Assert.True(File.Exists(Path.Combine(origin.Path, "lfs", "objects", oid[..2], oid[2..4], oid)), "the object reached the remote");

        // Someone else changes the image and pushes; pulling brings the picture, not a pointer.
        using var other = TempRepo.EmptyDirectory();
        other.RawWithEnv(new Dictionary<string, string> { ["GIT_LFS_SKIP_SMUDGE"] = "1" }, "clone", "-q", "-b", branch, origin.Path, other.Path);
        other.Raw("lfs", "install", "--local");
        other.Write("hero.png", v2);
        other.CommitAll("v2");
        other.Raw("push", "-q", "origin", "HEAD");
        await AppHarness.Until(() => remote.Action is RemoteAction.Fetch or RemoteAction.Push, "the fetch action");
        await remote.RunCommand.ExecuteAsync(null);
        await AppHarness.Until(() => remote.Action == RemoteAction.Pull, "the pull action");
        await remote.RunCommand.ExecuteAsync(null);
        Assert.Equal(v2, File.ReadAllBytes(repo.FullPath("hero.png")));

        // Switching branches checks out each branch's picture.
        await app.Tab.BranchList.LoadAsync();
        await app.Tab.BranchList.SwitchAsync(app.Tab.BranchList.Items.OfType<LocalBranchRow>().Single(r => r.Name == "first"));
        Assert.Equal("first", repo.CurrentBranch());
        Assert.Equal(v1, File.ReadAllBytes(repo.FullPath("hero.png")));
        await app.Tab.BranchList.LoadAsync();
        await app.Tab.BranchList.SwitchAsync(app.Tab.BranchList.Items.OfType<LocalBranchRow>().Single(r => r.Name == branch));
        Assert.Equal(v2, File.ReadAllBytes(repo.FullPath("hero.png")));
        Assert.Equal("", repo.Raw("status", "--porcelain").Trim());
        Assert.Empty(app.Main.Alerts);
    }
}
