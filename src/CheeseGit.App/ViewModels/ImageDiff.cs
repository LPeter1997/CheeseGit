using System.Windows.Input;
using Avalonia.Media.Imaging;
using CheeseGit.App.Services;
using CheeseGit.Core.Diff;
using CheeseGit.Core.Repository;

namespace CheeseGit.App.ViewModels;

/// <summary>One version of an image in an image diff: the decoded picture, or why there is none.</summary>
public sealed class ImageSide : IDisposable
{
    private ImageSide(DiffSide side, string label, long size, Bitmap? bitmap, string? problem, bool canDownload = false)
    {
        this.Side = side;
        this.Label = label;
        this.Size = size;
        this.Bitmap = bitmap;
        this.Problem = problem;
        this.CanDownload = canDownload;
    }

    public DiffSide Side { get; }

    /// <summary>"Before", "After", "Added", "Deleted".</summary>
    public string Label { get; }

    public bool IsOld => this.Side == DiffSide.Old;

    public bool IsNew => this.Side == DiffSide.New;

    public long Size { get; }

    public Bitmap? Bitmap { get; }

    public bool HasBitmap => this.Bitmap is not null;

    /// <summary>Why the picture cannot be shown (too large, not decodable, not downloaded from Git LFS), or null.</summary>
    public string? Problem { get; }

    /// <summary>A Git LFS version that is not on this computer and can be downloaded (the card's Download button).</summary>
    public bool CanDownload { get; }

    public int PixelWidth => this.Bitmap?.PixelSize.Width ?? 0;

    public int PixelHeight => this.Bitmap?.PixelSize.Height ?? 0;

    /// <summary>The size the picture is laid out at before fitting the frame (see <see cref="ImageDiff.Zoom"/>).</summary>
    public double DisplayWidth { get; private set; }

    public double DisplayHeight { get; private set; }

    /// <summary>Upscaled pixel art stays crisp; downscaled photos stay smooth.</summary>
    public bool Pixelated { get; private set; }

    /// <summary>"640 × 480 · 12.3 KB".</summary>
    public string Details => this.Bitmap is null ? ImageDiff.FormatSize(this.Size) : $"{this.PixelWidth} × {this.PixelHeight} · {ImageDiff.FormatSize(this.Size)}";

    internal void SetZoom(int zoom)
    {
        this.DisplayWidth = this.PixelWidth * zoom;
        this.DisplayHeight = this.PixelHeight * zoom;
        this.Pixelated = zoom > 1;
    }

    /// <summary>Decodes <paramref name="content"/>; call off the UI thread.</summary>
    internal static ImageSide Decode(DiffSide side, string label, FileContent content, LfsDownloadState missing)
    {
        if (content.IsMissing)
        {
            return missing switch
            {
                LfsDownloadState.Downloading => new ImageSide(side, label, content.Size, null, "Downloading from Git LFS…"),
                LfsDownloadState.Failed { Message: var message } => new ImageSide(side, label, content.Size, null, $"Could not download this version from Git LFS: {message}", canDownload: true),
                _ => new ImageSide(side, label, content.Size, null, "This version is stored in Git LFS and is not on this computer yet.", canDownload: true),
            };
        }
        if (content.Bytes is not { } bytes) return new ImageSide(side, label, content.Size, null, $"Too large to preview ({ImageDiff.FormatSize(content.Size)}).");
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            return new ImageSide(side, label, content.Size, new Bitmap(stream), null);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            // Skia throws for formats it has no codec for and for corrupt files; either way there is nothing to draw.
            return new ImageSide(side, label, content.Size, null, "This version cannot be previewed.");
        }
    }

    public void Dispose() => this.Bitmap?.Dispose();
}

/// <summary>
/// The image panel of the diff view: an image file's old and new version side by side (only one of them
/// for an added or deleted file), with dimensions and file sizes.
/// </summary>
public sealed class ImageDiff : IDisposable
{
    /// <summary>
    /// Larger versions are not read: the bytes and the decoded pixels are held while the diff is shown.
    /// Compressed images are far smaller than their pixels, so this also bounds decoding.
    /// </summary>
    public const long MaxBytes = 32 << 20;

    // Upscale small images (icons) by a whole factor to at least this size, so a 16 px change is visible.
    private const int TargetSize = 128;
    private const int MaxZoom = 8;

    // The formats Skia decodes. SVG is text: git diffs it as such.
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".apng", ".jpg", ".jpeg", ".jfif", ".gif", ".bmp", ".dib", ".webp", ".ico", ".cur", ".wbmp",
    };

    private ImageDiff(ImageSide? old, ImageSide? @new, ICommand? download)
    {
        this.DownloadCommand = download;
        this.Old = old;
        this.New = @new;
        var largest = Math.Max(Math.Max(old?.PixelWidth ?? 0, old?.PixelHeight ?? 0), Math.Max(@new?.PixelWidth ?? 0, @new?.PixelHeight ?? 0));
        // One zoom for both sides, so a change in dimensions shows as a change in size.
        this.Zoom = largest == 0 ? 1 : Math.Clamp(TargetSize / largest, 1, MaxZoom);
        old?.SetZoom(this.Zoom);
        @new?.SetZoom(this.Zoom);
    }

    public ImageSide? Old { get; }

    public ImageSide? New { get; }

    /// <summary>The versions in display order (old left, new right).</summary>
    public IReadOnlyList<ImageSide> Sides => this.Old is { } o ? this.New is { } n ? [o, n] : [o] : this.New is { } m ? [m] : [];

    public int Zoom { get; }

    /// <summary>Downloads the Git LFS versions that are not on this computer (the cards' Download button).</summary>
    public ICommand? DownloadCommand { get; }

    public bool IsZoomed => this.Zoom > 1;

    public string ZoomText => $"Shown at {this.Zoom}×";

    /// <summary>What changed between the versions: "800 × 600 → 1024 × 768 · 12.3 KB → 14.1 KB (+1.8 KB)"; empty with one side.</summary>
    public string Summary
    {
        get
        {
            if (this.Old is not { } o || this.New is not { } n) return "";
            var parts = new List<string>();
            if (o.HasBitmap && n.HasBitmap)
            {
                parts.Add(o.PixelWidth == n.PixelWidth && o.PixelHeight == n.PixelHeight
                    ? $"Same dimensions ({o.PixelWidth} × {o.PixelHeight})"
                    : $"{o.PixelWidth} × {o.PixelHeight} → {n.PixelWidth} × {n.PixelHeight}");
            }
            var delta = n.Size - o.Size;
            parts.Add(delta == 0 ? $"Same size ({FormatSize(n.Size)})" : $"{FormatSize(o.Size)} → {FormatSize(n.Size)} ({(delta > 0 ? "+" : "−")}{FormatSize(Math.Abs(delta))})");
            return string.Join(" · ", parts);
        }
    }

    public bool HasSummary => this.Summary.Length > 0;

    public static bool IsImagePath(FileDiff diff) =>
        Extensions.Contains(Path.GetExtension(DisplayText.Name(diff.Path))) || Extensions.Contains(Path.GetExtension(DisplayText.Name(diff.OldPath)));

    /// <summary>
    /// Decodes the versions; call off the UI thread. <see cref="Sides"/> is empty when neither exists.
    /// <paramref name="missing"/> says what is happening to Git LFS versions that are not on this computer.
    /// </summary>
    public static ImageDiff Decode(FileContent? old, FileContent? @new, LfsDownloadState? missing = null, ICommand? download = null)
    {
        var both = old is not null && @new is not null;
        missing ??= LfsDownloadState.NotDownloaded.Instance;
        return new ImageDiff(
            old is null ? null : ImageSide.Decode(DiffSide.Old, both ? "Before" : "Deleted", old, missing),
            @new is null ? null : ImageSide.Decode(DiffSide.New, both ? "After" : "Added", @new, missing),
            download);
    }

    public static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):0.#} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):0.##} GB",
    };

    public void Dispose()
    {
        this.Old?.Dispose();
        this.New?.Dispose();
    }
}

/// <summary>What is happening to a Git LFS version that is not on this computer.</summary>
public abstract record LfsDownloadState
{
    public sealed record NotDownloaded : LfsDownloadState
    {
        public static readonly NotDownloaded Instance = new();
    }

    public sealed record Downloading : LfsDownloadState
    {
        public static readonly Downloading Instance = new();
    }

    public sealed record Failed(string Message) : LfsDownloadState;
}
