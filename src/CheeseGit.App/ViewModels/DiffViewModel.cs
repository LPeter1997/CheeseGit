using Avalonia.Threading;
using CheeseGit.App.Services;
using CheeseGit.Core.Diff;
using CheeseGit.Core.Git;
using CheeseGit.Core.Repository;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CheeseGit.App.ViewModels;

/// <summary>
/// The right-hand panel: the diff of the file selected in the staging lists,
/// or of a file in the commit selected in the history.
/// </summary>
public sealed partial class DiffViewModel(RepoTabViewModel tab, DiffViewMode viewMode) : ObservableObject
{
    private int loadVersion;

    /// <summary>Where the shown diff comes from when it is neither the working tree nor the index (a commit's or a stash's file).</summary>
    private Func<GitRepository, Task<FileDiff>>? loader;

    public GitPath? Path { get; private set; }

    public DiffArea Area { get; private set; }

    /// <summary>The commit whose change is shown, or null for the working tree and index.</summary>
    public Oid? Commit { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFile), nameof(ShowPlaceholder))]
    public partial string? DisplayPath { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDiff), nameof(ShowDiff), nameof(CanSearch), nameof(CanEditLines), nameof(IsConflicted), nameof(ConflictHint))]
    public partial FileDiff? Diff { get; private set; }

    /// <summary>Why there is nothing to show (binary file, mode change only), or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage), nameof(ShowDiff), nameof(CanSearch))]
    public partial string? Message { get; private set; }

    /// <summary>An image file's versions, shown instead of the "binary file" message; null otherwise.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasImages), nameof(ShowDiff), nameof(CanSearch))]
    public partial ImageDiff? Images { get; private set; }

    /// <summary>
    /// The file is stored in Git LFS: git's diff is between pointers, so the panel shows the real
    /// content (an image) or a summary instead, and lines cannot be staged.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditLines))]
    public partial bool IsLfs { get; private set; }

    /// <summary>
    /// A pattern (<c>*.psd</c>) to offer tracking with Git LFS for the shown file: a large binary file
    /// added in the working tree or index, not in LFS yet. Null when there is no offer.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLfsTrackOffer), nameof(LfsTrackText), nameof(LfsTrackButtonText))]
    public partial string? LfsTrackPattern { get; private set; }

    /// <summary>The offered file's size, for the offer's text.</summary>
    private long lfsTrackSize;

    public bool HasLfsTrackOffer => this.LfsTrackPattern is not null;

    public string LfsTrackText =>
        $"A {ImageDiff.FormatSize(this.lfsTrackSize)} binary file. Git LFS keeps large files like this out of the repository's history, so clones and fetches stay fast.";

    public string LfsTrackButtonText => $"Track {this.LfsTrackPattern} with Git LFS";

    /// <summary>The shown LFS versions, for the Download button.</summary>
    private (FileDiff Diff, DiffSides Sides)? lfsShown;

    /// <summary>Added binary files from this size on get the offer; in a repository that uses LFS already, smaller ones too.</summary>
    internal const long LfsTrackThreshold = 1 << 20;

    internal const long LfsTrackThresholdInLfsRepo = 100 << 10;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsUnified), nameof(IsSplit))]
    public partial DiffViewMode ViewMode { get; set; } = viewMode;

    [ObservableProperty]
    public partial string SearchQuery { get; set; } = "";

    /// <summary>Index of the highlighted search match; the diff control scrolls to it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SearchStatus))]
    public partial int CurrentMatch { get; set; }

    /// <summary>Written by the diff control, which owns the displayed text.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SearchStatus))]
    public partial int MatchCount { get; set; }

    /// <summary>Incremented to ask the view to focus the search box (Ctrl+F).</summary>
    [ObservableProperty]
    public partial int FocusSearchRequest { get; private set; }

    public bool HasFile => this.DisplayPath is not null;

    /// <summary>"Copied!" beside the path for a moment after clicking it, like the hash in the history.</summary>
    [ObservableProperty]
    public partial bool PathCopied { get; private set; }

    /// <summary>
    /// Clicking the path in the header copies it. Selecting text would not work: the path is shortened from the
    /// middle when space runs out, so the shown text is not the path (a dev asked to copy it from there).
    /// </summary>
    [RelayCommand]
    private async Task CopyPath()
    {
        if (this.Path is not { } path) return;
        await tab.Shell.SetClipboardTextAsync(path.ToString());
        this.PathCopied = true;
        DispatcherTimer.RunOnce(() => this.PathCopied = false, TimeSpan.FromSeconds(1.5));
    }

    public bool ShowPlaceholder => this.DisplayPath is null;

    public bool HasDiff => this.Diff is not null;

    public bool HasMessage => this.Message is not null;

    public bool HasImages => this.Images is not null;

    public bool ShowDiff => this.Diff is not null && this.Message is null && this.Images is null;

    /// <summary>Search and the unified/split toggle act on text: not on images, nor on an LFS pointer's lines behind a summary.</summary>
    public bool CanSearch => this.ShowDiff && this.Diff is { Hunks.Count: > 0 };

    /// <summary>
    /// A conflicted file's diff is against HEAD, not the index: its lines cannot be staged. Nor can an
    /// LFS file's, whose lines are a pointer's (the engine refuses those too).
    /// </summary>
    public bool CanEditLines => this.Diff is not { IsConflicted: true } && !this.IsLfs;

    public bool IsConflicted => this.Diff is { IsConflicted: true };

    /// <summary>The bar over a conflicted file's diff: where to resolve it (a rebase has no conflict dialog).</summary>
    public string ConflictHint => tab.IsRebasing
        ? "Conflicted file: the working tree, with its conflict markers, against HEAD. Edit it to resolve the conflict, stage it, then continue the rebase."
        : "Conflicted file: the working tree, with its conflict markers, against the current branch. Lines cannot be staged; choose a version with Resolve conflicts.";

    public bool IsUnified => this.ViewMode == DiffViewMode.Unified;

    public bool IsSplit => this.ViewMode == DiffViewMode.Split;

    /// <summary>Line actions are offered in the staging view: stage from unstaged, unstage from staged.</summary>
    public bool IsStagedArea => this.Area == DiffArea.Staged;

    public string SearchStatus => this.SearchQuery.Length == 0 ? "" : this.MatchCount == 0 ? "No results" : $"{this.CurrentMatch + 1} of {this.MatchCount}";

    partial void OnImagesChanged(ImageDiff? oldValue, ImageDiff? newValue) => oldValue?.Dispose();

    partial void OnSearchQueryChanged(string value)
    {
        this.CurrentMatch = 0;
        this.OnPropertyChanged(nameof(this.SearchStatus));
    }

    partial void OnMatchCountChanged(int value)
    {
        if (this.CurrentMatch >= value) this.CurrentMatch = 0;
    }

    [RelayCommand]
    private void NextMatch()
    {
        if (this.MatchCount > 0) this.CurrentMatch = (this.CurrentMatch + 1) % this.MatchCount;
    }

    [RelayCommand]
    private void PreviousMatch()
    {
        if (this.MatchCount > 0) this.CurrentMatch = (this.CurrentMatch - 1 + this.MatchCount) % this.MatchCount;
    }

    [RelayCommand]
    private void FocusSearch() => this.FocusSearchRequest++;

    [RelayCommand]
    private void SetViewMode(DiffViewMode mode)
    {
        if (tab.Shell is MainViewModel main) main.SetDiffViewMode(mode);
        else this.ViewMode = mode;
    }

    public void Show(GitPath path, DiffArea area, string displayPath) => this.Show(path, area, null, displayPath);

    public void ShowCommit(Oid commit, GitPath path, string displayPath) => this.Show(path, DiffArea.Staged, commit, displayPath);

    /// <summary>Shows a diff read by <paramref name="load"/> (a file of a commit or a stash entry); read-only.</summary>
    public void ShowLoaded(GitPath path, string displayPath, Func<GitRepository, Task<FileDiff>> load)
    {
        this.loader = load;
        this.Path = path;
        this.Area = DiffArea.Staged;
        this.Commit = null;
        this.DisplayPath = displayPath;
        this.OnPropertyChanged(nameof(this.IsStagedArea));
        this.ResetContent();
        this.Reload();
    }

    private void Show(GitPath path, DiffArea area, Oid? commit, string displayPath)
    {
        var same = this.loader is null && this.Path is { } p && p == path && this.Area == area && this.Commit == commit;
        this.loader = null;
        this.Path = path;
        this.Area = area;
        this.Commit = commit;
        this.DisplayPath = displayPath;
        this.OnPropertyChanged(nameof(this.IsStagedArea));
        if (!same) this.ResetContent();
        this.Reload();
    }

    public void Clear()
    {
        this.loadVersion++;
        this.loader = null;
        this.Path = null;
        this.Commit = null;
        this.DisplayPath = null;
        this.ResetContent();
    }

    private void ResetContent()
    {
        this.Diff = null;
        this.Message = null;
        this.Images = null;
        this.IsLfs = false;
        this.LfsTrackPattern = null;
        this.lfsShown = null;
        this.SearchQuery = "";
    }

    /// <summary>Re-reads the shown file's diff (after any refresh: the file may have changed without its status changing).</summary>
    public async void Reload() => await this.LoadAsync(force: false);

    private async Task LoadAsync(bool force)
    {
        if (this.Path is not { } path) return;
        var area = this.Area;
        var commit = this.Commit;
        var loader = this.loader;
        var version = ++this.loadVersion;
        var diff = await tab.ReadAsync("Show diff", repo => loader is not null ? loader(repo) : commit is { } c ? repo.DiffCommitFileAsync(c, path) : repo.DiffFileAsync(path, area));
        if (version != this.loadVersion || diff is null) return;
        if (!force && this.Diff is { } current && SameDiff(current, diff)) return; // Keep the view (and its scroll position) as it is.
        var inStaging = loader is null && commit is null;
        // git diffs an LFS file's pointers: the panel shows the content they point to instead.
        var pointers = LfsPointer.IsPointerDiff(diff);
        DiffSides? sides = null;
        if (!diff.IsConflicted && (diff.IsBinary || pointers))
        {
            var image = ImageDiff.IsImagePath(diff);
            // The working tree's version is read from disk: its blob is not in the object database.
            var fromDisk = inStaging && area == DiffArea.Unstaged;
            var limit = image ? ImageDiff.MaxBytes : 0; // Other files: sizes and LFS pointers only.
            sides = await tab.ReadAsync(image ? "Read image" : "Read file versions", async repo => new DiffSides(
                await repo.ReadDiffSideAsync(diff, DiffSide.Old, fromDisk, limit),
                await repo.ReadDiffSideAsync(diff, DiffSide.New, fromDisk, limit)));
            if (version != this.loadVersion) return;
            var lfs = pointers || sides?.Old?.Lfs is not null || sides?.New?.Lfs is not null;
            this.lfsShown = lfs && sides is not null ? (diff, sides) : null;
            if (image && sides is not null && (sides.Old is not null || sides.New is not null))
            {
                await this.ShowImagesAsync(diff, sides, lfs, version);
                return;
            }
            if (lfs)
            {
                this.Diff = diff;
                this.Images = null;
                this.IsLfs = true;
                this.LfsTrackPattern = null;
                this.Message = LfsSummary(sides) + " There is no preview for this type of file.";
                return;
            }
        }
        this.Diff = diff;
        this.Images = null;
        this.IsLfs = false;
        this.OfferLfsTracking(diff, sides, inStaging);
        this.Message = Describe(diff, tab.IsRebasing
            ? "Conflicted file. Keep the version you want, stage it, then continue the rebase in the banner above."
            : "Conflicted file. Choose a version with Resolve conflicts in the banner above.");
    }

    /// <summary>
    /// Shows an image's versions. LFS versions that are not on this computer are downloaded first
    /// when they are small enough (<see cref="AppSettings.LfsAutoDownloadMegabytes"/>) and did not fail
    /// before; otherwise their cards offer the download.
    /// </summary>
    private async Task ShowImagesAsync(FileDiff diff, DiffSides sides, bool lfs, int version)
    {
        var missing = sides.Missing.ToList();
        var failure = missing.Select(m => tab.LfsDownloadFailure(m.Lfs!.Oid)).FirstOrDefault(f => f is not null);
        var auto = missing.Count > 0 && failure is null && missing.All(m => m.Size <= Math.Min(tab.LfsAutoDownloadBytes, ImageDiff.MaxBytes));
        LfsDownloadState state = auto ? LfsDownloadState.Downloading.Instance
            : failure is not null ? new LfsDownloadState.Failed(failure)
            : LfsDownloadState.NotDownloaded.Instance;
        var images = await Task.Run(() => ImageDiff.Decode(sides.Old, sides.New, state, this.DownloadLfsCommand));
        if (version != this.loadVersion)
        {
            images.Dispose();
            return;
        }
        this.Diff = diff;
        this.Message = null;
        this.IsLfs = lfs;
        this.LfsTrackPattern = null;
        this.Images = images;
        if (!lfs) this.OfferLfsTracking(diff, sides, this.loader is null && this.Commit is null);
        if (auto) await this.DownloadAsync(diff, sides, background: true);
    }

    /// <summary>The cards' Download button: every missing version of the shown file, whatever its size.</summary>
    [RelayCommand]
    private async Task DownloadLfs()
    {
        if (this.lfsShown is not var (diff, sides) || !ReferenceEquals(this.Diff, diff)) return;
        foreach (var m in sides.Missing) tab.ForgetLfsDownloadFailure(m.Lfs!.Oid);
        this.Images = await Task.Run(() => ImageDiff.Decode(sides.Old, sides.New, LfsDownloadState.Downloading.Instance, this.DownloadLfsCommand));
        await this.DownloadAsync(diff, sides, background: false);
    }

    private async Task DownloadAsync(FileDiff diff, DiffSides sides, bool background)
    {
        var old = sides.Old is { IsMissing: true } o ? await tab.DownloadLfsAsync(o.Lfs!, ImageDiff.MaxBytes, background) ?? o : sides.Old;
        var @new = sides.New is { IsMissing: true } n ? await tab.DownloadLfsAsync(n.Lfs!, ImageDiff.MaxBytes, background) ?? n : sides.New;
        // A refresh that found the same diff keeps this.Diff; anything else replaced it and shows its own versions.
        if (!ReferenceEquals(this.Diff, diff)) return;
        var downloaded = new DiffSides(old, @new);
        var failure = downloaded.Missing.Select(m => tab.LfsDownloadFailure(m.Lfs!.Oid)).FirstOrDefault(f => f is not null);
        var images = await Task.Run(() => ImageDiff.Decode(old, @new, failure is null ? LfsDownloadState.NotDownloaded.Instance : new LfsDownloadState.Failed(failure), this.DownloadLfsCommand));
        if (!ReferenceEquals(this.Diff, diff))
        {
            images.Dispose();
            return;
        }
        this.lfsShown = (diff, downloaded);
        this.Images = images;
    }

    /// <summary>"Stored in Git LFS (12.3 KB → 14 KB)."</summary>
    private static string LfsSummary(DiffSides? sides) => sides switch
    {
        { Old: { } o, New: { } n } => $"Stored in Git LFS ({ImageDiff.FormatSize(o.Size)} → {ImageDiff.FormatSize(n.Size)}).",
        { Old: { } o } => $"Stored in Git LFS ({ImageDiff.FormatSize(o.Size)}).",
        { New: { } n } => $"Stored in Git LFS ({ImageDiff.FormatSize(n.Size)}).",
        _ => "Stored in Git LFS.",
    };

    /// <summary>Offers <c>git lfs track *.ext</c> for a large binary file that is new in the working tree or index.</summary>
    private void OfferLfsTracking(FileDiff diff, DiffSides? sides, bool inStaging)
    {
        this.LfsTrackPattern = null;
        if (!inStaging || !diff.IsBinary || diff.Change != DiffChange.Added || sides?.New is not { Lfs: null } added || !tab.CanTrackWithLfs) return;
        if (added.Size < (tab.LfsSetup == LfsSetup.NotUsed ? LfsTrackThreshold : LfsTrackThresholdInLfsRepo)) return;
        var extension = System.IO.Path.GetExtension(DisplayText.Name(diff.Path));
        // A pattern git-lfs writes to .gitattributes as is: no spaces, no attribute syntax.
        if (extension.Length < 2 || extension.Any(c => char.IsWhiteSpace(c) || c is '#' or '!' or '"' or '\\' or '[' or '*' or '?')) return;
        this.lfsTrackSize = added.Size;
        this.LfsTrackPattern = "*" + extension;
    }

    [RelayCommand]
    private async Task TrackWithLfs()
    {
        if (this.LfsTrackPattern is not { } pattern || this.Path is not { } path) return;
        if (!await tab.MainShell.ConfirmAsync(
            $"Store every {pattern} file in Git LFS from now on? This adds {pattern} to .gitattributes, which you commit like any other change. Files committed before stay as they are.",
            "Track with Git LFS", destructive: false)) return;
        IReadOnlyCollection<GitPath> staged = this.Area == DiffArea.Staged ? [path] : [];
        if (await tab.RunAsync($"Track {pattern} with Git LFS", (repo, ct) => repo.TrackWithLfsAsync(pattern, staged, ct), SnapshotParts.Status))
        {
            this.LfsTrackPattern = null;
            await tab.CheckLfsAsync();
        }
    }

    private static string? Describe(FileDiff diff, string conflictMessage)
    {
        if (diff.IsBinary) return "Binary file — no text diff to show.";
        if (diff.Hunks.Count > 0) return null;
        if (diff.IsConflicted) return conflictMessage;
        if (diff.OldMode is { } from && diff.NewMode is { } to && from != to) return $"File mode changed from {from} to {to}.";
        return diff.Change switch
        {
            DiffChange.Added => "Empty file.",
            DiffChange.Deleted => "Deleted empty file.",
            DiffChange.Renamed => $"Renamed from {DisplayText.Path(diff.OldPath)} without changes.",
            _ => "No changes to show.",
        };
    }

    private static bool SameDiff(FileDiff a, FileDiff b)
    {
        if (a.Path != b.Path || a.OldBlob != b.OldBlob || a.NewBlob != b.NewBlob || a.IsBinary != b.IsBinary || a.IsConflicted != b.IsConflicted || a.OldMode != b.OldMode || a.NewMode != b.NewMode || a.Hunks.Count != b.Hunks.Count) return false;
        for (var i = 0; i < a.Hunks.Count; i++)
        {
            var (x, y) = (a.Hunks[i], b.Hunks[i]);
            if (x.Header != y.Header || x.Lines.Count != y.Lines.Count) return false;
            for (var j = 0; j < x.Lines.Count; j++)
            {
                if (x.Lines[j].Kind != y.Lines[j].Kind || !x.Lines[j].Content.AsSpan().SequenceEqual(y.Lines[j].Content)) return false;
            }
        }
        return true;
    }

    /// <summary>A gutter click in the diff control: stage, unstage or (with Shift) discard these lines.</summary>
    public async Task ApplyLinesAsync(IReadOnlyList<LineSelection> selections, bool discard)
    {
        if (this.Diff is not { } diff || selections.Count == 0 || this.Path is not { } path) return;
        var name = DisplayText.Name(path);
        var area = this.Area;
        if (discard)
        {
            await tab.RunAsync($"Discard lines in {name}", (repo, ct) => repo.DiscardLinesAsync(diff, selections, area, ct), SnapshotParts.Status);
        }
        else if (area == DiffArea.Unstaged)
        {
            await tab.RunAsync($"Stage lines in {name}", (repo, ct) => repo.StageLinesAsync(diff, selections, ct), SnapshotParts.Status);
        }
        else
        {
            await tab.RunAsync($"Unstage lines in {name}", (repo, ct) => repo.UnstageLinesAsync(diff, selections, ct), SnapshotParts.Status);
        }
    }
}

/// <summary>A diff's two versions as read for previews (null: the side does not exist).</summary>
public sealed record DiffSides(FileContent? Old, FileContent? New)
{
    /// <summary>The Git LFS versions that are not on this computer.</summary>
    public IEnumerable<FileContent> Missing => new[] { this.Old, this.New }.OfType<FileContent>().Where(s => s.IsMissing);
}
