using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CheeseGit.Core.Diff;
using CheeseGit.Core.Git;
using CheeseGit.Core.Repository;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CheeseGit.App.ViewModels;

/// <summary>One file in the staged or unstaged list.</summary>
public sealed partial class FileRow(FileChange change, DiffArea area) : ObservableObject
{
    public FileChange Change { get; } = change;

    public GitPath Path => this.Change.Path;

    public DiffArea Area { get; } = area;

    public string DisplayPath { get; } = DisplayText.Path(change.Path);

    /// <summary>"old → new" for renames, shown as the tooltip.</summary>
    public string ToolTip { get; } = change.OriginalPath is { } from ? $"{DisplayText.Path(from)} → {DisplayText.Path(change.Path)}" : DisplayText.Path(change.Path);

    public string Letter => this.Change.Status switch
    {
        FileStatus.Added => "A",
        FileStatus.Deleted => "D",
        FileStatus.Renamed => "R",
        FileStatus.Copied => "C",
        FileStatus.TypeChanged => "T",
        FileStatus.Untracked => "U",
        FileStatus.Conflicted => "!",
        _ => "M",
    };

    public string StatusName => this.Change.Status.ToString();

    // The real path, not the display text: control characters are escaped for display only.
    internal Task CopyPathAsync(IAppShell shell) => shell.SetClipboardTextAsync(this.Path.ToString());

    internal Task CopyNameAsync(IAppShell shell) => shell.SetClipboardTextAsync(this.Path.Name);

    /// <summary>Stored in Git LFS: the row shows an "LFS" tag.</summary>
    public bool IsLfs => this.Change.IsLfs;

    // Badge colours of the old app's FileStatusBadge.
    public bool IsGreen => this.Change.Status is FileStatus.Added or FileStatus.Untracked;

    public bool IsAccent => this.Change.Status is FileStatus.Modified or FileStatus.TypeChanged;

    public bool IsRed => this.Change.Status is FileStatus.Deleted or FileStatus.Conflicted;

    public bool IsMuted => this.Change.Status is FileStatus.Renamed or FileStatus.Copied;

    /// <summary>Part of the multi-selection.</summary>
    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>The file whose diff is shown.</summary>
    [ObservableProperty]
    public partial bool IsCurrent { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AdditionsText), nameof(DeletionsText))]
    public partial FileStats? Stats { get; set; }

    public string AdditionsText => this.Stats is { Additions: > 0 } s ? "+" + DisplayText.Count(s.Additions) : "";

    public string DeletionsText => this.Stats is { Deletions: > 0 } s ? "−" + DisplayText.Count(s.Deletions) : "";

    /// <summary>Same file with the same kind of change: the row can be kept (and its selection with it).</summary>
    internal bool SameAs(FileChange other) => this.Change == other;
}

/// <summary>A list that can be replaced in one notification; one per 1,000 inserts would freeze on huge worktrees.</summary>
public sealed class BulkObservableCollection<T> : ObservableCollection<T>
{
    public void ReplaceAll(IEnumerable<T> items)
    {
        this.Items.Clear();
        foreach (var item in items) this.Items.Add(item);
        this.OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(this.Count)));
        this.OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs("Item[]"));
        this.OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}

/// <summary>One of the two file lists (unstaged or staged) with its filter, selection and stats.</summary>
public sealed partial class FileListViewModel(StagingViewModel owner, DiffArea area) : ObservableObject
{
    // Above this, small edits are applied as one reset instead of item by item.
    private const int IncrementalLimit = 200;

    private List<FileRow> all = [];
    private Dictionary<GitPath, FileStats> stats = [];
    private FileRow? anchor;

    public DiffArea Area { get; } = area;

    public bool IsStaged => this.Area == DiffArea.Staged;

    public string Title => this.IsStaged ? "Staged Changes" : "Unstaged Changes";

    /// <summary>The rows that pass the filter, in path order.</summary>
    public BulkObservableCollection<FileRow> Rows { get; } = [];

    public IReadOnlyList<FileRow> All => this.all;

    public int TotalCount => this.all.Count;

    public bool IsEmpty => this.all.Count == 0;

    public bool HasRows => this.Rows.Count > 0;

    public bool NothingMatches => this.all.Count > 0 && this.Rows.Count == 0;

    public string EmptyText => this.IsStaged ? "No staged changes." : "No unstaged changes.";

    /// <summary>Old app: an empty staged list offers an empty commit, as the clean working tree does.</summary>
    public bool ShowEmptyCommitLink => this.IsStaged && this.IsEmpty && owner.ShowEmptyCommitLink;

    public IRelayCommand EnableEmptyCommitCommand => owner.EnableEmptyCommitCommand;

    public string NoMatchText => this.IsStaged ? "No staged files match your filter." : "No unstaged files match your filter.";

    public IEnumerable<FileRow> Selected => this.Rows.Where(r => r.IsSelected);

    public int SelectedCount => this.Rows.Count(r => r.IsSelected);

    [ObservableProperty]
    public partial string TotalAdditions { get; private set; } = "";

    [ObservableProperty]
    public partial string TotalDeletions { get; private set; } = "";

    /// <summary>Width of the per-row stats columns so numbers line up (old app: computeStatWidths).</summary>
    [ObservableProperty]
    public partial double AdditionsWidth { get; private set; }

    [ObservableProperty]
    public partial double DeletionsWidth { get; private set; }

    public bool ShiftHeld => owner.ShiftHeld;

    /// <summary>"Stage All ↓", "Stage 3 Selected ↓", or the discard variants while Shift is held.</summary>
    public string BulkActionText
    {
        get
        {
            var n = this.SelectedCount;
            if (this.ShiftHeld) return n > 1 ? $"Discard {n} Selected ✕" : "Discard All ✕";
            if (this.IsStaged) return n > 1 ? $"↑ Unstage {n} Selected" : "↑ Unstage All";
            return n > 1 ? $"Stage {n} Selected ↓" : "Stage All ↓";
        }
    }

    public string BulkActionToolTip
    {
        get
        {
            var n = this.SelectedCount;
            var what = this.IsStaged ? "staged" : "unstaged";
            if (this.ShiftHeld) return n > 1 ? $"Discard {n} selected {what} changes" : $"Discard all {what} changes";
            var verb = this.IsStaged ? "Unstage" : "Stage";
            return n > 1 ? $"{verb} {n} selected files" : $"{verb} all changes";
        }
    }

    internal void NotifyEmptyCommitLink() => this.OnPropertyChanged(nameof(this.ShowEmptyCommitLink));

    internal void NotifyShift()
    {
        this.OnPropertyChanged(nameof(this.ShiftHeld));
        this.NotifySelection();
    }

    private void NotifySelection()
    {
        this.OnPropertyChanged(nameof(this.SelectedCount));
        this.OnPropertyChanged(nameof(this.BulkActionText));
        this.OnPropertyChanged(nameof(this.BulkActionToolTip));
    }

    /// <summary>Takes the new list of changes, keeping row objects (and their selection) for unchanged files.</summary>
    internal void SetChanges(IReadOnlyList<FileChange> changes, string filter)
    {
        var old = this.all.ToDictionary(r => r.Path);
        this.all = changes.Select(c => old.TryGetValue(c.Path, out var row) && row.SameAs(c) ? row : new FileRow(c, this.Area)).ToList();
        foreach (var row in this.all) row.Stats = this.stats.GetValueOrDefault(row.Path);
        if (this.anchor is not null && !this.all.Contains(this.anchor)) this.anchor = null;
        this.ApplyFilter(filter);
        this.OnPropertyChanged(nameof(this.TotalCount));
        this.OnPropertyChanged(nameof(this.IsEmpty));
        this.OnPropertyChanged(nameof(this.ShowEmptyCommitLink));
    }

    internal void ApplyFilter(string filter)
    {
        var visible = filter.Length == 0
            ? this.all
            : this.all.Where(r => r.DisplayPath.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var row in this.Rows.Except(visible)) row.IsSelected = false;
        UpdateRows(this.Rows, visible);
        this.OnPropertyChanged(nameof(this.HasRows));
        this.OnPropertyChanged(nameof(this.NothingMatches));
        this.NotifySelection();
    }

    /// <summary>
    /// Brings <paramref name="rows"/> to <paramref name="target"/> (both in path
    /// order) with item-level changes when there are few, so the list keeps its
    /// scroll position; a reset otherwise.
    /// </summary>
    private static void UpdateRows(BulkObservableCollection<FileRow> rows, IReadOnlyList<FileRow> target)
    {
        var keep = target.ToHashSet();
        var removals = rows.Count(r => !keep.Contains(r));
        if (removals + Math.Max(0, target.Count - (rows.Count - removals)) > IncrementalLimit)
        {
            rows.ReplaceAll(target);
            return;
        }
        for (var i = rows.Count - 1; i >= 0; i--)
        {
            if (!keep.Contains(rows[i])) rows.RemoveAt(i);
        }
        for (var i = 0; i < target.Count; i++)
        {
            if (i < rows.Count && ReferenceEquals(rows[i], target[i])) continue;
            rows.Insert(i, target[i]);
        }
    }

    internal void SetStats(IReadOnlyList<FileStats> list)
    {
        this.stats = list.GroupBy(s => s.Path).ToDictionary(g => g.Key, g => g.Last());
        int adds = 0, dels = 0, addChars = 0, delChars = 0;
        foreach (var row in this.all)
        {
            // An LFS file's counts are its pointer's lines (+3 −3), which say nothing about the file.
            row.Stats = row.IsLfs ? null : this.stats.GetValueOrDefault(row.Path);
            if (row.Stats is not { } s) continue;
            adds += s.Additions;
            dels += s.Deletions;
            addChars = Math.Max(addChars, row.AdditionsText.Length);
            delChars = Math.Max(delChars, row.DeletionsText.Length);
        }
        this.TotalAdditions = adds > 0 ? "+" + DisplayText.Count(adds) : "";
        this.TotalDeletions = dels > 0 ? "−" + DisplayText.Count(dels) : "";
        // About one digit width of the 11 px UI font.
        this.AdditionsWidth = addChars * 7;
        this.DeletionsWidth = delChars * 7;
    }

    /// <summary>Click semantics of the old app: plain click selects and shows the diff, Ctrl toggles, Shift selects a range.</summary>
    public void Click(FileRow row, bool ctrl, bool shift)
    {
        owner.ClearSelection(except: this);
        if (shift)
        {
            var from = this.anchor is not null ? this.Rows.IndexOf(this.anchor) : -1;
            var to = this.Rows.IndexOf(row);
            if (from < 0) from = to;
            if (!ctrl)
            {
                foreach (var r in this.Rows) r.IsSelected = false;
            }
            for (var i = Math.Min(from, to); i <= Math.Max(from, to); i++) this.Rows[i].IsSelected = true;
        }
        else if (ctrl)
        {
            row.IsSelected = !row.IsSelected;
            this.anchor = row;
        }
        else
        {
            foreach (var r in this.Rows) r.IsSelected = r == row;
            this.anchor = row;
            owner.ShowDiff(row);
        }
        this.NotifySelection();
    }

    /// <summary>Arrow keys: move the current file up or down.</summary>
    public void Move(int delta)
    {
        if (this.Rows.Count == 0) return;
        var current = this.Rows.FirstOrDefault(r => r.IsCurrent);
        var index = current is null ? (delta > 0 ? 0 : this.Rows.Count - 1) : Math.Clamp(this.Rows.IndexOf(current) + delta, 0, this.Rows.Count - 1);
        this.Click(this.Rows[index], ctrl: false, shift: false);
    }

    internal void ClearSelection()
    {
        foreach (var r in this.Rows) r.IsSelected = false;
        this.anchor = null;
        this.NotifySelection();
    }

    internal FileRow? Find(GitPath path) => this.all.FirstOrDefault(r => r.Path == path);

    [RelayCommand]
    private Task BulkAction() => owner.BulkActionAsync(this);

    // Concurrent: the command is shared by every row, and an AsyncRelayCommand refuses clicks while
    // it runs, so arrows clicked before the previous stage had refreshed were dropped. The session
    // serializes the git calls; a discard's confirmation is modal.
    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task RowAction(FileRow row) => owner.RowActionAsync(row);

    [RelayCommand]
    private Task CopyPath(FileRow row) => row.CopyPathAsync(owner.Shell);

    [RelayCommand]
    private Task CopyName(FileRow row) => row.CopyNameAsync(owner.Shell);
}

/// <summary>The staging tab: both file lists, the filter and the commit form.</summary>
public sealed partial class StagingViewModel : ObservableObject
{
    // Counting lines reads every untracked file; past this many, untracked files show no counts.
    private const int MaxUntrackedStats = 2000;

    private readonly RepoTabViewModel tab;
    private int statsVersion;

    internal IAppShell Shell => this.tab.Shell;

    public StagingViewModel(RepoTabViewModel tab)
    {
        this.tab = tab;
        this.Unstaged = new FileListViewModel(this, DiffArea.Unstaged);
        this.Staged = new FileListViewModel(this, DiffArea.Staged);
        tab.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(RepoTabViewModel.ShiftHeld))
            {
                this.Unstaged.NotifyShift();
                this.Staged.NotifyShift();
            }
            else if (e.PropertyName == nameof(RepoTabViewModel.Snapshot))
            {
                this.NotifyCommitState();
            }
        };
    }

    public FileListViewModel Unstaged { get; }

    public FileListViewModel Staged { get; }

    public bool ShiftHeld => this.tab.ShiftHeld;

    [ObservableProperty]
    public partial string Filter { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsClean), nameof(HasChanges))]
    public partial bool Loaded { get; private set; }

    public bool IsClean => this.Loaded && this.Unstaged.IsEmpty && this.Staged.IsEmpty;

    public bool HasChanges => this.Loaded && !this.IsClean;

    // ── Commit form ───────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCommit), nameof(CanStash))]
    [NotifyCanExecuteChangedFor(nameof(CommitCommand), nameof(StashStagedCommand))]
    public partial string Summary { get; set; } = "";

    [ObservableProperty]
    public partial string Description { get; set; } = "";

    /// <summary>Suggested summary when exactly one file is staged ("Update README.md"), used when Summary is empty.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SummaryPlaceholder), nameof(CanCommit), nameof(CanStash))]
    [NotifyCanExecuteChangedFor(nameof(CommitCommand), nameof(StashStagedCommand))]
    public partial string DefaultSummary { get; private set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCommit), nameof(CommitButtonText), nameof(ShowEmptyCommitLink))]
    [NotifyCanExecuteChangedFor(nameof(CommitCommand))]
    public partial bool EmptyCommitMode { get; set; }

    partial void OnEmptyCommitModeChanged(bool value) => this.Staged.NotifyEmptyCommitLink();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCommit), nameof(CanStash), nameof(CommitButtonText))]
    [NotifyCanExecuteChangedFor(nameof(CommitCommand), nameof(StashStagedCommand))]
    public partial bool Committing { get; private set; }

    public string SummaryPlaceholder => this.DefaultSummary.Length > 0 ? this.DefaultSummary : "Summary (required)";

    private string EffectiveSummary => this.Summary.Trim().Length > 0 ? this.Summary.Trim() : this.DefaultSummary;

    /// <summary>
    /// A paused merge, revert or cherry-pick: a plain commit would finish it with this form's
    /// message (or fail on unmerged files). The conflict dialog finishes it with git's message.
    /// </summary>
    private bool InOperation => this.tab.HasOperation;

    public bool CanCommit => !this.tab.BrowsingHistory && !this.InOperation && this.tab.IsLoaded && (this.EmptyCommitMode || !this.Staged.IsEmpty)
        && this.EffectiveSummary.Length > 0 && !this.Committing;

    public bool CanStash => !this.tab.BrowsingHistory && !this.InOperation && !this.Staged.IsEmpty && this.EffectiveSummary.Length > 0 && !this.Committing;

    public bool ShowMoreActions => !this.tab.BrowsingHistory && !this.InOperation && !this.Staged.IsEmpty;

    public bool ShowEmptyCommitLink => !this.EmptyCommitMode && !this.tab.BrowsingHistory && !this.InOperation;

    public string CommitButtonText =>
        this.Committing ? "Committing…"
        : this.InOperation ? $"{this.tab.OperationName} in progress"
        : this.tab.BrowsingHistory ? "Viewing history"
        : this.EmptyCommitMode ? $"Make empty commit to {this.tab.BranchText}"
        : $"Commit to {this.tab.BranchText}";

    public string? CommitToolTip =>
        this.tab.IsRebasing ? "Stage the resolved files, then continue the rebase in the banner"
        : this.InOperation ? $"Finish or abort the {this.tab.OperationName.ToLowerInvariant()} with Resolve conflicts in the banner"
        : this.tab.BrowsingHistory ? "Cannot commit while viewing history — jump back to present or create a new branch"
        : !this.EmptyCommitMode && this.Staged.IsEmpty ? "No staged files to commit"
        : this.EffectiveSummary.Length == 0 ? "A commit summary is required"
        : "Commit (Ctrl+Enter)";

    private void NotifyCommitState()
    {
        this.OnPropertyChanged(nameof(this.CanCommit));
        this.OnPropertyChanged(nameof(this.CanStash));
        this.OnPropertyChanged(nameof(this.ShowMoreActions));
        this.OnPropertyChanged(nameof(this.ShowEmptyCommitLink));
        this.Staged.NotifyEmptyCommitLink();
        this.OnPropertyChanged(nameof(this.CommitButtonText));
        this.OnPropertyChanged(nameof(this.CommitToolTip));
        this.CommitCommand.NotifyCanExecuteChanged();
        this.StashStagedCommand.NotifyCanExecuteChanged();
    }

    partial void OnFilterChanged(string value)
    {
        var filter = value.Trim();
        this.Unstaged.ApplyFilter(filter);
        this.Staged.ApplyFilter(filter);
        this.Unstaged.ClearSelection();
        this.Staged.ClearSelection();
    }

    [RelayCommand]
    private void ClearFilter() => this.Filter = "";

    /// <summary>Projects a new snapshot onto the lists. Nothing here is a second source of truth: it is rebuilt from the snapshot every time.</summary>
    internal void Apply(RepoSnapshot snapshot, RepoSnapshot? previous)
    {
        var filter = this.Filter.Trim();
        var statusChanged = previous is null || !ReferenceEquals(previous.Status, snapshot.Status);
        if (statusChanged)
        {
            this.Unstaged.SetChanges(snapshot.Status.Unstaged.ToList(), filter);
            this.Staged.SetChanges(snapshot.Status.Staged.ToList(), filter);
            // The old app: a status change cancels empty-commit mode.
            if (previous is not null && this.EmptyCommitMode && !snapshot.Status.Entries.SequenceEqual(previous.Status.Entries)) this.EmptyCommitMode = false;
        }
        this.Loaded = true;
        this.OnPropertyChanged(nameof(this.IsClean));
        this.OnPropertyChanged(nameof(this.HasChanges));
        this.DefaultSummary = SuggestSummary(this.Staged.All);
        this.NotifyCommitState();
        this.SyncCurrentFile();
    }

    internal static string SuggestSummary(IReadOnlyList<FileRow> staged)
    {
        if (staged.Count != 1) return "";
        var c = staged[0].Change;
        var name = DisplayText.Name(c.Path);
        return c.Status switch
        {
            FileStatus.Added or FileStatus.Untracked => $"Add {name}",
            FileStatus.Deleted => $"Delete {name}",
            FileStatus.Renamed => $"Rename {name}",
            FileStatus.Copied => $"Copy {name}",
            _ => $"Update {name}",
        };
    }

    /// <summary>
    /// Keeps the diff on a file that still exists in its list. When it left
    /// the list (staged, discarded, committed), the old app moved on to the
    /// first remaining file of that list.
    /// </summary>
    private void SyncCurrentFile()
    {
        var diff = this.tab.Diff;
        if (diff.Path is not { } path) return;
        var list = diff.Area == DiffArea.Staged ? this.Staged : this.Unstaged;
        var row = list.Find(path);
        if (row is null)
        {
            var next = list.Rows.FirstOrDefault();
            if (next is null) diff.Clear();
            else this.ShowDiff(next);
        }
        this.MarkCurrent();
    }

    private void MarkCurrent()
    {
        var diff = this.tab.Diff;
        foreach (var list in new[] { this.Unstaged, this.Staged })
        {
            foreach (var r in list.All) r.IsCurrent = diff.Path is { } p && list.Area == diff.Area && r.Path == p;
        }
    }

    internal void ShowDiff(FileRow row)
    {
        this.tab.Diff.Show(row.Path, row.Area, row.DisplayPath);
        this.MarkCurrent();
    }

    internal void ClearSelection(FileListViewModel except)
    {
        if (except != this.Unstaged) this.Unstaged.ClearSelection();
        if (except != this.Staged) this.Staged.ClearSelection();
    }

    internal async void ReloadStats()
    {
        if (this.tab.Snapshot is not { } snapshot) return;
        var version = ++this.statsVersion;
        var untracked = snapshot.Status.Entries.Where(e => e.Kind == EntryKind.Untracked).Select(e => e.Path).ToList();
        if (untracked.Count > MaxUntrackedStats) untracked = [];
        var unstaged = this.tab.ReadAsync("Read diff stats", repo => repo.DiffStatsAsync(DiffArea.Unstaged, untracked));
        var staged = this.tab.ReadAsync("Read diff stats", repo => repo.DiffStatsAsync(DiffArea.Staged));
        var (u, s) = (await unstaged, await staged);
        if (version != this.statsVersion) return; // A newer refresh is on its way.
        if (u is not null) this.Unstaged.SetStats(u);
        if (s is not null) this.Staged.SetStats(s);
    }

    // ── File actions ──────────────────────────────────────────────────

    /// <summary>The arrow (or ✕ with Shift) on a row.</summary>
    internal async Task RowActionAsync(FileRow row)
    {
        if (this.ShiftHeld)
        {
            await this.DiscardAsync([row], row.Area, $"Discard changes to \"{DisplayText.Name(row.Path)}\"? This cannot be undone.");
            return;
        }
        if (row.Area == DiffArea.Unstaged && !await this.ConfirmStagingConflictsAsync([row])) return;
        this.MoveCurrentAwayFrom([row]);
        var name = DisplayText.Name(row.Path);
        if (row.Area == DiffArea.Unstaged) await this.tab.RunAsync($"Stage {name}", (repo, ct) => repo.StageAsync([row.Path], ct), SnapshotParts.Status);
        else await this.tab.RunAsync($"Unstage {name}", (repo, ct) => repo.UnstageAsync([row.Path], ct), SnapshotParts.Status);
    }

    /// <summary>The list header's button: all files, or the multi-selection when more than one row is selected.</summary>
    internal async Task BulkActionAsync(FileListViewModel list)
    {
        var selected = list.Selected.ToList();
        var rows = selected.Count > 1 ? selected : list.All.ToList();
        if (rows.Count == 0) return;
        var what = list.IsStaged ? "staged" : "unstaged";
        if (this.ShiftHeld)
        {
            var message = selected.Count > 1
                ? $"Discard {rows.Count} selected {what} change{(rows.Count == 1 ? "" : "s")}? This cannot be undone."
                : $"Discard all {rows.Count} {what} change{(rows.Count == 1 ? "" : "s")}? This cannot be undone.";
            await this.DiscardAsync(rows, list.Area, message);
            return;
        }
        if (!list.IsStaged && !await this.ConfirmStagingConflictsAsync(rows)) return;
        this.MoveCurrentAwayFrom(rows);
        list.ClearSelection();
        var paths = rows.Select(r => r.Path).ToList();
        var label = $"{(list.IsStaged ? "Unstage" : "Stage")} {paths.Count} file{(paths.Count == 1 ? "" : "s")}";
        if (list.IsStaged) await this.tab.RunAsync(label, (repo, ct) => repo.UnstageAsync(paths, ct), SnapshotParts.Status);
        else await this.tab.RunAsync(label, (repo, ct) => repo.StageAsync(paths, ct), SnapshotParts.Status);
    }

    /// <summary>
    /// Staging a conflicted file marks it resolved, markers and all, and the next commit takes it
    /// as it is. Asks first when a file to stage still has conflict markers.
    /// </summary>
    private async Task<bool> ConfirmStagingConflictsAsync(IReadOnlyList<FileRow> rows)
    {
        var conflicted = rows.Where(r => r.Change.Status == FileStatus.Conflicted).Select(r => r.Path).ToHashSet();
        if (conflicted.Count == 0) return true;
        var counts = await this.tab.ReadAsync("Read conflict markers", repo => repo.ConflictCountsAsync());
        var marked = counts?.Where(c => c.ConflictCount > 0 && conflicted.Contains(c.Path)).Select(c => DisplayText.Name(c.Path)).ToList() ?? [];
        if (marked.Count == 0) return true;
        var what = marked.Count == 1 ? $"\"{marked[0]}\" still has" : $"{marked.Count} files still have";
        return await this.tab.Shell.ConfirmAsync(
            $"{what} conflict markers. Staging marks the conflict resolved with the markers left in the file. Stage anyway?",
            "Stage anyway");
    }

    private async Task DiscardAsync(IReadOnlyList<FileRow> rows, DiffArea area, string message)
    {
        if (!await this.tab.Shell.ConfirmAsync(message, "Discard")) return;
        var paths = rows.Select(r => r.Path).ToList();
        if (this.tab.Diff.Path is { } current && this.tab.Diff.Area == area && paths.Contains(current)) this.tab.Diff.Clear();
        this.Unstaged.ClearSelection();
        this.Staged.ClearSelection();
        var label = paths.Count == 1 ? $"Discard {DisplayText.Name(paths[0])}" : $"Discard {paths.Count} files";
        if (area == DiffArea.Unstaged) await this.tab.RunAsync(label, (repo, ct) => repo.DiscardUnstagedAsync(paths, ct), SnapshotParts.Status);
        else await this.tab.RunAsync(label, (repo, ct) => repo.DiscardStagedAsync(paths, ct), SnapshotParts.Status);
    }

    /// <summary>
    /// When the shown file is about to leave its list, show its neighbour
    /// instead (previous, else next), as the old app did for single-file
    /// actions; bulk actions clear the diff.
    /// </summary>
    private void MoveCurrentAwayFrom(IReadOnlyList<FileRow> rows)
    {
        var diff = this.tab.Diff;
        if (diff.Path is not { } current || rows.Count == 0 || rows[0].Area != diff.Area || !rows.Any(r => r.Path == current)) return;
        var list = diff.Area == DiffArea.Staged ? this.Staged : this.Unstaged;
        if (rows.Count > 1)
        {
            diff.Clear();
            this.MarkCurrent();
            return;
        }
        var index = list.Rows.IndexOf(rows[0]);
        var neighbour = index > 0 ? list.Rows[index - 1] : index + 1 < list.Rows.Count ? list.Rows[index + 1] : null;
        if (neighbour is null) diff.Clear();
        else this.ShowDiff(neighbour);
        this.MarkCurrent();
    }

    // ── Commit ────────────────────────────────────────────────────────

    [RelayCommand]
    private void EnableEmptyCommit() => this.EmptyCommitMode = true;

    [RelayCommand(CanExecute = nameof(CanCommit))]
    private async Task Commit()
    {
        var summary = this.EffectiveSummary;
        var description = this.Description.Trim();
        var allowEmpty = this.EmptyCommitMode;
        this.Committing = true;
        try
        {
            var oid = await this.tab.RunAsync("Commit", async (repo, ct) => (object)await repo.CommitAsync(summary, description, allowEmpty, ct));
            if (oid is not Oid commit) return;
            this.Summary = "";
            this.Description = "";
            this.EmptyCommitMode = false;
            this.tab.Diff.Clear();
            this.MarkCurrent();
            this.tab.Shell.ShowInfo($"Committed {commit.ToShortHex()} “{summary}” to {this.tab.BranchText}", "Undo", async () =>
            {
                // Undo moves the branch back one commit: only right while this commit is still the tip.
                if (this.tab.Snapshot?.Head is { BrowsingHistory: false, Commit: { } head } && head == commit) await this.UndoLastCommitAsync();
                else this.tab.Shell.ShowError($"Cannot undo {commit.ToShortHex()}: it is no longer the latest commit on the branch.");
            });
        }
        finally
        {
            this.Committing = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanStash))]
    private async Task StashStaged()
    {
        var message = this.EffectiveSummary;
        this.Committing = true;
        try
        {
            if (!await this.tab.RunAsync("Stash staged changes", (repo, ct) => repo.StashStagedAsync(message, ct))) return;
            this.Summary = "";
            this.Description = "";
            this.tab.Diff.Clear();
            this.MarkCurrent();
        }
        finally
        {
            this.Committing = false;
        }
    }

    /// <summary>Moves the branch back one commit, keeping its changes staged and its message in the form.</summary>
    public async Task UndoLastCommitAsync()
    {
        var restored = await this.tab.RunAsync("Undo last commit", (repo, ct) => repo.UndoLastCommitAsync(ct), blocking: true);
        if (restored is null) return;
        this.Summary = restored.Summary;
        this.Description = restored.Description;
        this.tab.LeftTab = LeftPanelTab.Staging;
    }
}
