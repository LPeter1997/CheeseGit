using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CheeseGit.App.ViewModels;
using CheeseGit.Core.Tests;

namespace CheeseGit.App.Tests;

/// <summary>Image files show their versions side by side instead of "binary file".</summary>
public class ImageDiffTests
{
    /// <summary>A PNG of <paramref name="width"/> × <paramref name="height"/>, encoded by Skia.</summary>
    internal static byte[] Png(int width, int height, uint argb = 0xFF3366CC)
    {
        using var bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using (var buffer = bitmap.Lock())
        {
            var pixels = new int[buffer.RowBytes / 4 * height];
            Array.Fill(pixels, unchecked((int)argb));
            System.Runtime.InteropServices.Marshal.Copy(pixels, 0, buffer.Address, pixels.Length);
        }
        using var stream = new MemoryStream();
        bitmap.Save(stream, new PngBitmapEncoderOptions());
        return stream.ToArray();
    }

    private static async Task<ImageDiff> ImagesAsync(DiffViewModel diff, string what)
    {
        await AppHarness.Until(() => diff.Images is not null, what);
        Assert.Null(diff.Message);
        Assert.False(diff.ShowDiff);
        return diff.Images!;
    }

    [AvaloniaFact]
    public async Task A_modified_image_shows_both_versions_with_their_dimensions_and_sizes()
    {
        using var repo = TempRepo.Init();
        repo.Write("logo.png", Png(40, 20));
        repo.CommitAll("logo");
        var after = Png(60, 30, 0xFFCC3333);
        repo.Write("logo.png", after);
        using var app = await AppHarness.OpenAsync(repo);

        await app.ShowAsync(app.Unstaged("logo.png"));
        var images = await ImagesAsync(app.Diff, "the image versions");
        Assert.Equal(["Before", "After"], images.Sides.Select(s => s.Label));
        Assert.Equal((40, 20), (images.Old!.PixelWidth, images.Old.PixelHeight));
        Assert.Equal((60, 30), (images.New!.PixelWidth, images.New.PixelHeight));
        Assert.Equal(after.Length, images.New.Size);
        Assert.StartsWith("40 × 20 → 60 × 30 · ", images.Summary);
        // Small images are scaled up by one whole factor, so both keep their relative size.
        Assert.Equal(2, images.Zoom);
        Assert.Equal(120, images.New.DisplayWidth);
        Assert.False(app.Diff.CanSearch);
    }

    [AvaloniaFact]
    public async Task Staged_added_and_deleted_images_show_one_version()
    {
        using var repo = TempRepo.Init();
        repo.Write("old.png", Png(8, 8));
        repo.CommitAll("old");
        repo.Delete("old.png");
        repo.Write("new.png", Png(300, 200));
        repo.Raw("add", "new.png");
        using var app = await AppHarness.OpenAsync(repo);

        await app.ShowAsync(app.StagedRow("new.png"));
        var added = await ImagesAsync(app.Diff, "the staged image");
        Assert.Equal("Added", Assert.Single(added.Sides).Label);
        Assert.Equal(300, added.New!.PixelWidth);
        Assert.Equal(1, added.Zoom);
        Assert.False(added.HasSummary);

        await app.ShowAsync(app.Unstaged("old.png"));
        await AppHarness.Until(() => app.Diff.Images is { Old: not null }, "the deleted image");
        var deleted = app.Diff.Images!;
        Assert.Equal("Deleted", Assert.Single(deleted.Sides).Label);
        Assert.Equal(8, deleted.Old!.PixelHeight);
        Assert.Equal(8, deleted.Zoom); // capped
    }

    [AvaloniaFact]
    public async Task An_untracked_image_is_read_from_disk_and_follows_edits()
    {
        using var repo = TempRepo.WithCommit();
        repo.Write("shot.png", Png(10, 10));
        using var app = await AppHarness.OpenAsync(repo);

        await app.ShowAsync(app.Unstaged("shot.png"));
        var first = await ImagesAsync(app.Diff, "the untracked image");
        Assert.Equal(10, first.New!.PixelWidth);

        // The diff of an edited binary file looks the same (no hunks): the blob ids tell them apart.
        repo.Write("shot.png", Png(20, 10));
        await AppHarness.Until(() => app.Diff.Images is { New.PixelWidth: 20 }, "the edited image");
    }

    [AvaloniaFact]
    public async Task A_commit_image_and_an_undecodable_one_show_in_the_history()
    {
        using var repo = TempRepo.Init();
        repo.Write("icon.png", Png(16, 16));
        repo.Write("broken.png", [0, 1, 2, 3, 0, 5]);
        repo.CommitAll("icons");
        repo.Write("icon.png", Png(32, 32));
        repo.CommitAll("bigger icon");
        using var app = await AppHarness.OpenAsync(repo);
        app.Tab.LeftTab = LeftPanelTab.History;
        var history = app.Tab.History;
        await AppHarness.Until(() => history.Rows.Count == 2, "the history to load");

        history.Select(history.Rows[0]);
        await AppHarness.Until(() => history.Details.Files.Count == 1, "the commit's files");
        history.Details.SelectFile(history.Details.Files[0]);
        var images = await ImagesAsync(history.Diff, "the commit's image");
        Assert.Equal((16, 32), (images.Old!.PixelWidth, images.New!.PixelWidth));

        history.Select(history.Rows[1]);
        await AppHarness.Until(() => history.Details.Files.Count == 2, "the first commit's files");
        history.Details.SelectFile(history.Details.Files.Single(f => f.DisplayPath == "broken.png"));
        await AppHarness.Until(() => history.Diff.Images is { New.HasBitmap: false }, "the broken image");
        Assert.Equal("This version cannot be previewed.", history.Diff.Images!.New!.Problem);
        Assert.Equal("6 B", history.Diff.Images.New.Details);
    }
}
