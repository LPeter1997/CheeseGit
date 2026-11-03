using System.Collections.Concurrent;
using System.Globalization;
using Avalonia.Threading;
using CheeseGit.Core.Diff;
using CheeseGit.Core.Git;
using CheeseGit.Core.Repository;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CheeseGit.App.ViewModels;

/// <summary>
/// One history row. Text arrives asynchronously: the row is drawn at once with
/// its graph and fills in when the background reader delivers its metadata.
/// Reading a text property is what requests it, so only realized (visible)
/// rows are ever read.
/// </summary>
public sealed partial class CommitRow(HistoryViewModel owner, int row, Oid oid) : ObservableObject
{
    private CommitInfo? info;
    private bool requested;

    public HistoryViewModel Owner { get; } = owner;

    public int Row { get; } = row;

    public Oid Oid { get; } = oid;

    public string ShortHash { get; } = oid.ToShortHex();

    public bool IsHead => this.Owner.HeadOid == this.Oid;

    public bool IsLocalOnly => this.Owner.View?.IsLocalOnly(this.Row) ?? false;

    /// <summary>Undo is offered on the commit HEAD's branch points to (not while browsing history).</summary>
    public bool CanUndo => this.IsHead && !this.Owner.BrowsingHistory;

    public string Summary => this.Info?.Summary ?? "";

    private static readonly System.ComponentModel.PropertyChangedEventArgs TextOpacityChanged = new(nameof(TextOpacity));

    /// <summary>While a dot is hovered, rows outside its lane chain fade (old app: to 30%).</summary>
    public double TextOpacity =>
        this.Owner.GraphFade > 0 && this.Owner.View is { } view && this.Row < view.Count && view.Layout.NodeColor(this.Row) != this.Owner.HighlightColor ? 0.3 : 1;

    internal void NotifyTextOpacity() => this.OnPropertyChanged(TextOpacityChanged);

    public string Author => this.Info?.Author.Name ?? "";

    public string Date => this.Info is { } i ? RelativeDate(i.Author.When) : "";

    public string ToolTip => this.Info is { } i ? $"{i.Summary}\n{i.Author.Name} <{i.Author.Email}>\n{i.Author.When.LocalDateTime:yyyy-MM-dd HH:mm}" : "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AdditionsText), nameof(DeletionsText), nameof(HasStats))]
    public partial CommitStats? Stats { get; private set; }

    public string AdditionsText => this.Stats is { Insertions: > 0 } s ? "+" + DisplayText.Count(s.Insertions) : "";

    public string DeletionsText => this.Stats is { Deletions: > 0 } s ? "−" + DisplayText.Count(s.Deletions) : "";

    public bool HasStats => this.Stats is { } s && (s.Insertions > 0 || s.Deletions > 0);

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    [ObservableProperty]
    public partial bool HashCopied { get; private set; }

    internal CommitInfo? Info
    {
        get
        {
            if (this.info is null && this.MarkRequested()) this.Owner.Request(this);
            return this.info;
        }
    }

    /// <summary>True the first time: the row's metadata has not been asked for yet.</summary>
    internal bool MarkRequested()
    {
        if (this.info is not null || this.requested) return false;
        this.requested = true;
        return true;
    }

    internal void Deliver(CommitInfo info, CommitStats? stats)
    {
        this.info = info;
        this.Stats = stats;
        this.OnPropertyChanged(nameof(this.Summary));
        this.OnPropertyChanged(nameof(this.Author));
        this.OnPropertyChanged(nameof(this.Date));
        this.OnPropertyChanged(nameof(this.ToolTip));
    }

    internal void DeliverStats(CommitStats stats) => this.Stats = stats;

    internal void RefreshHead()
    {
        this.OnPropertyChanged(nameof(this.IsHead));
        this.OnPropertyChanged(nameof(this.CanUndo));
    }

    [RelayCommand]
    private async Task CopyHash()
    {
        await this.Owner.Tab.Shell.SetClipboardTextAsync(this.Oid.ToHex());
        this.HashCopied = true;
        DispatcherTimer.RunOnce(() => this.HashCopied = false, TimeSpan.FromSeconds(1.5));
    }

    /// <summary>The old app's <c>formatRelativeDate</c>.</summary>
    public static string RelativeDate(DateTimeOffset when, DateTimeOffset? now = null)
    {
        var elapsed = (now ?? DateTimeOffset.Now) - when;
        var minutes = (int)Math.Floor(elapsed.TotalMinutes);
        if (minutes < 1) return "just now";
        if (minutes < 60) return $"{minutes}m ago";
        var hours = minutes / 60;
        if (hours < 24) return $"{hours}h ago";
        var days = hours / 24;
        if (days == 1) return "yesterday";
        if (days < 30) return $"{days} days ago";
        // The app runs with invariant globalization, where "d" is the US month/day/year whatever
        // the user's locale: ISO dates read the same everywhere.
        return when.LocalDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }
}

/// <summary>A section header in the branch dropdown.</summary>
public sealed record BranchListHeader(string Text)
{
    public static readonly BranchListHeader Remote = new("Remote");
}

/// <summary>A branch in the graph's visibility list.</summary>
public sealed partial class GraphBranch(HistoryViewModel owner, string fullName, bool isRequired) : ObservableObject
{
    private const string Heads = "refs/heads/";
    private const string Remotes = "refs/remotes/";

    public string FullName { get; } = fullName;

    public bool IsRemote { get; } = fullName.StartsWith(Remotes, StringComparison.Ordinal);

    /// <summary>The branch name; for a remote branch, without the remote (shown beside it, as in the old app).</summary>
    public string Name { get; } = fullName.StartsWith(Heads, StringComparison.Ordinal) ? fullName[Heads.Length..]
        : RemoteParts(fullName) is { } parts ? parts.Branch
        : fullName;

    public string? Remote { get; } = RemoteParts(fullName)?.Remote;

    /// <summary>
    /// "origin/feature/x" splits at the first slash, like the old app: git allows slashes in
    /// remote names too, but the refs alone cannot tell where such a name ends.
    /// </summary>
    internal static (string Remote, string Branch)? RemoteParts(string fullName)
    {
        if (!fullName.StartsWith(Remotes, StringComparison.Ordinal)) return null;
        var rest = fullName[Remotes.Length..];
        var slash = rest.IndexOf('/');
        return slash > 0 ? (rest[..slash], rest[(slash + 1)..]) : null;
    }

    /// <summary>The current branch and the branches it was built on cannot be hidden: hiding them would hide nothing.</summary>
    public bool IsRequired { get; } = isRequired;

    [ObservableProperty]
    public partial bool IsVisible { get; set; }

    public string ToolTip => this.IsRequired ? "Required branch (always visible)" : this.IsVisible ? "Hide from graph" : "Show in graph";

    partial void OnIsVisibleChanged(bool value) => this.OnPropertyChanged(nameof(this.ToolTip));

    [RelayCommand]
    private void Toggle()
    {
        if (!this.IsRequired) owner.SetVisible(this, !this.IsVisible);
    }
}

/// <summary>
/// The history tab: the commit graph of the visible branches and the selected
/// commit's files and diff. The topology is loaded once and again when refs or
/// HEAD change; hiding and showing branches lays out the loaded DAG again
/// without asking git (docs/architecture.md, Graph).
/// </summary>
public sealed partial class HistoryViewModel : ObservableObject, IDisposable
{
    /// <summary>Commits whose metadata is read ahead of a requested row, so scrolling finds them ready.</summary>
    private const int Prefetch = 32;

    private readonly ConcurrentDictionary<Oid, CommitInfo> infoCache = new();
    private readonly ConcurrentDictionary<Oid, CommitStats> statsCache = new();
    // Most recent request first: while scrolling fast, what is on screen now
    // matters more than rows that have already scrolled past.
    private readonly ConcurrentStack<CommitRow> requests = new();
    private readonly SemaphoreSlim pending = new(0);
    private readonly CancellationTokenSource stop = new();
    private readonly HashSet<string> userVisible = new(StringComparer.Ordinal);
    private Thread? reader;
    private HistoryGraph? graph;
    private bool loadQueued;
    private bool loading;
    private bool userChoseVisibility;
    private bool commitGraphChecked;
    private Oid? selectedOid;

    public HistoryViewModel(RepoTabViewModel tab, Services.DiffViewMode viewMode)
    {
        this.Tab = tab;
        this.Diff = new DiffViewModel(tab, viewMode);
        this.Details = new ChangeDetailsViewModel(tab, this.Diff, "Select a commit to view its changes.");
    }

    public RepoTabViewModel Tab { get; }

    /// <summary>The diff of the file selected in the commit's file list.</summary>
    public DiffViewModel Diff { get; }

    /// <summary>The selected commit's files and the selected file's diff.</summary>
    public ChangeDetailsViewModel Details { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty), nameof(HasRows))]
    public partial HistoryView? View { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty), nameof(HasRows))]
    public partial IReadOnlyList<CommitRow> Rows { get; private set; } = [];

    /// <summary>What the list shows: every row, or the search's matches.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NoMatches))]
    public partial IReadOnlyList<CommitRow> DisplayRows { get; private set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSearching), nameof(NoMatches), nameof(TextMargin))]
    public partial string SearchQuery { get; set; } = "";

    /// <summary>The query <see cref="DisplayRows"/> answers ("" for every row); it lags <see cref="SearchQuery"/> while a search runs.</summary>
    public string ShownQuery { get; private set; } = "";

    /// <summary>The search index is being read (one git log over the loaded history).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NoMatches))]
    public partial bool SearchPending { get; private set; }

    /// <summary>While searching, rows are not adjacent, so the graph is hidden (old app).</summary>
    public bool IsSearching => this.SearchQuery.Trim().Length > 0;

    public bool NoMatches => this.IsSearching && !this.SearchPending && this.DisplayRows.Count == 0;

    /// <summary>The text column's margin: the graph's column provides the left gap, except while searching.</summary>
    // 6 px after the graph: the HEAD ring (radius 7) on the last lane touched the summary.
    public Avalonia.Thickness TextMargin => this.IsSearching ? new(12, 0, 12, 0) : new(6, 0, 12, 0);

    [ObservableProperty]
    public partial bool IsLoading { get; private set; }

    /// <summary>Lanes the graph column reserves: the widest of the rows on screen (Phase 0).</summary>
    [ObservableProperty]
    public partial int GraphLanes { get; private set; } = 1;

    /// <summary>
    /// The dropdown's branches: local ones, then remote branches with no local branch of the same
    /// name (old app). A remote branch that has a local one (origin/main beside main) gets no row of
    /// its own; "Show all" still draws it, as the old app drew the remote's default branch.
    /// </summary>
    public IReadOnlyList<GraphBranch> Branches { get; private set; } = [];

    /// <summary><see cref="Branches"/> as the dropdown lists them, with a "Remote" header before the remote ones.</summary>
    public IReadOnlyList<object> BranchItems { get; private set; } = [];

    /// <summary>Remote branches left out of <see cref="Branches"/> because a local branch has their name.</summary>
    private IReadOnlyList<string> trackedRemotes = [];

    public bool AllBranchesVisible => this.Branches.All(b => b.IsVisible);

    public string ToggleAllText => this.AllBranchesVisible ? "Hide all" : "Show all";

    public bool IsEmpty => this.View is not null && this.Rows.Count == 0;

    public bool HasRows => this.Rows.Count > 0;

    public Oid? HeadOid => this.Tab.Snapshot?.Head.Commit;

    public bool BrowsingHistory => this.Tab.BrowsingHistory;

    // A rebase detaches HEAD, so it looks like browsing history; switching back would leave the rebase stranded.
    public bool ShowJumpToPresent => this.BrowsingHistory && !this.Tab.IsRebasing;

    /// <summary>
    /// The branch whose history is being browsed: the one that was current when
    /// a commit was checked out from here. Without it (history browsing started
    /// elsewhere), the engine's guess from <see cref="HeadState.Branch"/>.
    /// </summary>
    public string? Anchor { get; private set; }

    /// <summary>The branch that keeps lane 0 and cannot be hidden.</summary>
    public string? MainBranch =>
        this.Tab.Snapshot?.Head is { } head && (head.BrowsingHistory ? this.Anchor ?? head.Branch : head.Branch) is { } name ? "refs/heads/" + name : null;

    // ── Selected commit ───────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(ContextLabel))]
    public partial CommitRow? Selected { get; private set; }

    public bool HasSelection => this.Selected is not null;

    /// <summary>"abc1234 - summary" above the diff, as in the old app.</summary>
    public string? ContextLabel => this.Selected is { } row ? $"{row.ShortHash} - {row.Summary}" : null;

    // ── Loading ───────────────────────────────────────────────────────

    /// <summary>Loads the history the first time the tab is shown.</summary>
    public void EnsureLoaded()
    {
        if (this.graph is null && !this.loading) this.Reload();
    }

    /// <summary>Called with every published snapshot; reloads when refs, HEAD or remotes changed.</summary>
    /// <summary>Another remote was chosen: local-only markers and the remote's default branch change.</summary>
    internal void OnRemoteChanged()
    {
        if (this.graph is not null || this.loading) this.Reload();
    }

    internal void OnSnapshot(SnapshotParts changed)
    {
        this.OnPropertyChanged(nameof(this.BrowsingHistory));
        this.OnPropertyChanged(nameof(this.ShowJumpToPresent));
        if ((changed & SnapshotParts.Head) != 0)
        {
            if (this.Tab.Snapshot?.Head.BrowsingHistory == false) this.Anchor = null;
            foreach (var row in this.Rows) row.RefreshHead();
        }
        if (this.graph is null && !this.loading) return; // Never shown: load on demand.
        if ((changed & (SnapshotParts.Refs | SnapshotParts.Head | SnapshotParts.Remotes)) != 0) this.Reload();
    }

    /// <summary>Completes when no load is running (or queued): the rows and the branch list match the snapshot.</summary>
    internal Task WhenIdleAsync() => this.idle?.Task ?? Task.CompletedTask;

    private TaskCompletionSource? idle;

    private async void Reload()
    {
        if (this.loading)
        {
            this.loadQueued = true;
            return;
        }
        this.loading = true;
        this.idle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        this.IsLoading = this.graph is null;
        try
        {
            do
            {
                this.loadQueued = false;
                var remote = this.Tab.Remote.Active;
                var graph = await this.Tab.ReadAsync("Load history", repo => repo.LoadHistoryAsync(new HistoryRequest(Remote: remote)));
                if (graph is null || this.stop.IsCancellationRequested) return;
                this.graph = graph;
                await this.LayoutAsync();
                _ = this.OfferCommitGraphAsync(graph.Dag.Count);
            }
            while (this.loadQueued && !this.stop.IsCancellationRequested);
        }
        finally
        {
            this.loading = false;
            this.IsLoading = false;
            this.idle?.TrySetResult();
            this.idle = null;
        }
    }

    /// <summary>
    /// From this many commits on, a commit-graph is offered. Without one, the topology of 100k
    /// commits loads in 850 ms, with one in 180 ms; at 20k the difference is still ~170 ms per load.
    /// </summary>
    internal int CommitGraphOfferCommits { get; set; } = 20_000;

    /// <summary>Offers to write a commit-graph (docs/architecture.md, Git engine) once per repository, ever, if it would help.</summary>
    private async Task OfferCommitGraphAsync(int commits)
    {
        if (this.commitGraphChecked || commits < CommitGraphOfferCommits) return;
        this.commitGraphChecked = true;
        // ReadAsync returns null on failure, so the answer travels boxed.
        var missing = await this.Tab.ReadAsync("Check commit-graph", async repo => (object)await repo.CommitGraphMissingAsync()) is true;
        if (!missing || this.stop.IsCancellationRequested || !this.Tab.MarkCommitGraphOffered()) return;
        this.Tab.Shell.ShowInfo(
            $"This repository has {DisplayText.Count(commits)} commits and no commit-graph file. Writing one (git commit-graph write) makes loading the history about 4× faster.",
            "Write commit-graph",
            this.WriteCommitGraphAsync,
            sticky: true);
    }

    internal async Task WriteCommitGraphAsync()
    {
        if (await this.Tab.RunAsync("Write commit-graph", (repo, ct) => repo.WriteCommitGraphAsync(ct), SnapshotParts.None))
        {
            this.Tab.Shell.ShowInfo("Commit-graph written: the history now loads faster.");
        }
    }

    /// <summary>The default remote (old app): origin, else the first. The remote button can choose another.</summary>
    internal static string? PickRemote(IReadOnlyList<RemoteInfo> remotes) =>
        remotes.FirstOrDefault(r => r.Name == "origin")?.Name ?? remotes.FirstOrDefault()?.Name;

    /// <summary>Filters and lays out the loaded DAG for the visible branches, off the UI thread.</summary>
    private async Task LayoutAsync()
    {
        if (this.graph is not { } graph) return;
        var main = this.MainBranch;
        var head = this.HeadOid;
        var required = HistoryView.RequiredBranches(graph, main);
        // First load shows only the required branches (old app); later loads keep the user's choices.
        var visible = this.userChoseVisibility ? required.Concat(this.userVisible.Where(graph.Tips.ContainsKey)).ToList() : [.. required];
        var view = await Task.Run(() => HistoryView.Create(graph, visible, main, head));
        if (this.stop.IsCancellationRequested || !ReferenceEquals(graph, this.graph)) return;

        var shown = view.VisibleBranches.ToHashSet(StringComparer.Ordinal);
        var all = graph.Branches.Select(b => new GraphBranch(this, b, required.Contains(b)) { IsVisible = shown.Contains(b) }).ToList();
        var localNames = all.Where(b => !b.IsRemote).Select(b => b.Name).ToHashSet(StringComparer.Ordinal);
        var tracked = all.Where(b => b.IsRemote && !b.IsRequired && (b.Name == "HEAD" || localNames.Contains(b.Name))).ToList();
        var locals = all.Where(b => !b.IsRemote)
            .OrderBy(b => !b.IsRequired)
            .ThenBy(b => b.Name, StringComparer.OrdinalIgnoreCase);
        var remotes = all.Where(b => b.IsRemote).Except(tracked)
            .OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(b => b.Remote, StringComparer.OrdinalIgnoreCase)
            .ToList();
        this.Branches = [.. locals, .. remotes];
        this.trackedRemotes = [.. tracked.Select(b => b.FullName)];
        this.BranchItems = remotes.Count == 0 ? [.. this.Branches] : [.. locals, BranchListHeader.Remote, .. remotes];
        this.OnPropertyChanged(nameof(this.Branches));
        this.OnPropertyChanged(nameof(this.BranchItems));
        this.OnPropertyChanged(nameof(this.AllBranchesVisible));
        this.OnPropertyChanged(nameof(this.ToggleAllText));
        this.Apply(view);
    }

    private void Apply(HistoryView view)
    {
        this.requests.Clear();
        var rows = new CommitRow[view.Count];
        for (var i = 0; i < rows.Length; i++)
        {
            var oid = view.Dag.OidAt(i);
            var row = new CommitRow(this, i, oid);
            if (this.infoCache.TryGetValue(oid, out var info)) row.Deliver(info, this.statsCache.TryGetValue(oid, out var s) ? s : null);
            rows[i] = row;
        }
        this.GraphFade = 0; // The hovered chain's colour means nothing in the new layout.
        this.View = view;
        this.Rows = rows;
        if (this.IsSearching)
        {
            _ = this.SearchAsync(debounce: false);
        }
        else
        {
            this.ShownQuery = "";
            this.DisplayRows = rows;
        }
        this.GraphLanes = Math.Max(1, Math.Min(view.Layout.LaneCount, 4));

        // Keep the selected commits selected when they are still shown.
        this.picked.RemoveWhere(oid => view.Dag.IndexOf(oid) < 0);
        foreach (var oid in this.picked) rows[view.Dag.IndexOf(oid)].IsSelected = true;
        this.OnPropertyChanged(nameof(this.PickedCount));
        this.OnPropertyChanged(nameof(this.HasPicked));
        this.OnPropertyChanged(nameof(this.CherryPickLabel));
        var selected = this.selectedOid is { } oid2 && view.Dag.IndexOf(oid2) is var index and >= 0 ? rows[index] : null;
        if (selected is not null)
        {
            selected.IsSelected = true;
            this.Selected = selected;
        }
        else if (this.Selected is not null)
        {
            this.ClearSelection();
        }
    }

    internal void SetVisible(GraphBranch branch, bool visible)
    {
        this.userChoseVisibility = true;
        this.CaptureVisible();
        if (visible) this.userVisible.Add(branch.FullName);
        else this.userVisible.Remove(branch.FullName);
        branch.IsVisible = visible;
        _ = this.LayoutAsync();
    }

    [RelayCommand]
    private void ToggleAll()
    {
        this.userChoseVisibility = true;
        var showAll = !this.AllBranchesVisible;
        this.userVisible.Clear();
        if (showAll)
        {
            foreach (var b in this.Branches) this.userVisible.Add(b.FullName);
            foreach (var name in this.trackedRemotes) this.userVisible.Add(name);
        }
        _ = this.LayoutAsync();
    }

    private void CaptureVisible()
    {
        if (this.userVisible.Count > 0) return;
        foreach (var b in this.Branches.Where(b => b.IsVisible && !b.IsRequired)) this.userVisible.Add(b.FullName);
        if (this.View is { } view) foreach (var name in this.trackedRemotes.Where(view.VisibleBranches.Contains)) this.userVisible.Add(name);
    }

    // ── Hover highlighting ────────────────────────────────────────────

    /// <summary>
    /// The lane chain (layout colour) of the dot under the pointer. It stays set after the pointer
    /// leaves, so the fade back animates from the right chain.
    /// </summary>
    [ObservableProperty]
    public partial int HighlightColor { get; private set; } = -1;

    /// <summary>
    /// 1 while a dot is hovered: the graph fades every other chain (to 10%) and the other rows'
    /// text (to 30%), as the old app faded every other branch. The old app found a commit's branch
    /// with the ownership walk the rewrite dropped (#59); a colour chain is what the eye follows
    /// as one line, and the layout knows it exactly.
    /// </summary>
    [ObservableProperty]
    public partial double GraphFade { get; private set; }

    private int visibleFirst;
    private int visibleLast = -1;

    /// <summary>The graph reports the dot under the pointer, or null when it leaves.</summary>
    public void HoverDot(CommitRow? row)
    {
        if (row is not null && !this.IsSearching && this.View is { } view && row.Row < view.Count)
        {
            this.HighlightColor = view.Layout.NodeColor(row.Row);
            this.GraphFade = 1;
        }
        else
        {
            this.GraphFade = 0;
        }
        this.NotifyTextOpacity();
    }

    /// <summary>Rows off screen read the opacity when they are realized, so only those on screen are told.</summary>
    private void NotifyTextOpacity()
    {
        var rows = this.DisplayRows;
        for (var i = Math.Max(0, this.visibleFirst - 4); i <= Math.Min(rows.Count - 1, this.visibleLast + 4); i++) rows[i].NotifyTextOpacity();
    }

    /// <summary>The view reports which rows are on screen; the graph column follows their widest lane.</summary>
    public void SetVisibleRange(int first, int last)
    {
        (this.visibleFirst, this.visibleLast) = (first, last);
        if (this.View is not { } view || view.Count == 0) return;
        first = Math.Clamp(first, 0, view.Count - 1);
        last = Math.Clamp(last, first, view.Count - 1);
        var lanes = 1;
        for (var row = first; row <= last; row++)
        {
            lanes = Math.Max(lanes, view.Layout.NodeLane(row) + 1);
            foreach (var s in view.Layout.SegmentsOf(row)) lanes = Math.Max(lanes, Math.Max(s.FromLane, s.ToLane) + 1);
        }
        this.GraphLanes = lanes;
    }

    // ── Metadata ──────────────────────────────────────────────────────

    /// <summary>Asks for a row's metadata (and the rows after it). Never blocks.</summary>
    internal void Request(CommitRow row)
    {
        var rows = this.Rows;
        var end = Math.Min(rows.Count, row.Row + Prefetch);
        // Pushed furthest first, so the requested row pops first.
        for (var r = end - 1; r > row.Row; r--)
        {
            if (ReferenceEquals(rows[r].Owner, this) && rows[r].MarkRequested()) this.Push(rows[r]);
        }
        this.Push(row);
    }

    private void Push(CommitRow row)
    {
        this.requests.Push(row);
        this.pending.Release();
        if (this.reader is null)
        {
            this.reader = new Thread(this.ReadLoop) { IsBackground = true, Name = "commit metadata" };
            this.reader.Start();
        }
    }

    // The only thread that reads metadata for the list. Results are batched
    // into one UI-thread post per burst, not one per row (Phase 0: reading
    // metadata on the UI thread cost p95 18 → 32 ms frames).
    private void ReadLoop()
    {
        var batch = new List<(CommitRow Row, CommitInfo Info)>();
        try
        {
            while (true)
            {
                this.pending.Wait(this.stop.Token);
                if (this.Tab.Session?.Repository is not { } repo) continue;
                do
                {
                    if (!this.requests.TryPop(out var row)) continue;
                    var info = this.infoCache.GetOrAdd(row.Oid, oid => repo.GetCommit(oid) ?? Missing(oid));
                    batch.Add((row, info));
                }
                while (batch.Count < 256 && this.pending.Wait(0));

                // Text first: line totals take a git process per batch (and
                // long for big commits), so they follow in a second post.
                var delivered = batch.Select(b => (b.Row, b.Info, Stats: this.statsCache.TryGetValue(b.Row.Oid, out var s) ? s : null)).ToArray();
                batch.Clear();
                // Nothing reaches the dispatcher once the tab is closed.
                this.stop.Token.ThrowIfCancellationRequested();
                Dispatcher.UIThread.Post(() =>
                {
                    foreach (var (row, info, stats) in delivered) row.Deliver(info, stats);
                    if (this.Selected is { } selected && delivered.Any(d => d.Row == selected)) this.OnPropertyChanged(nameof(this.ContextLabel));
                });

                var needStats = delivered.Where(d => d.Stats is null).Select(d => d.Row).ToList();
                if (needStats.Count == 0) continue;
                try
                {
                    using var _ = GitRunner.BeginOperation("Read commit stats", background: true);
                    var oids = needStats.Select(r => r.Oid).Distinct().ToList();
                    foreach (var (oid, stats) in repo.CommitStatsAsync(oids, this.stop.Token).GetAwaiter().GetResult()) this.statsCache[oid] = stats;
                }
                catch (GitException)
                {
                    continue; // Stats are decoration; the rows still show their text.
                }
                this.stop.Token.ThrowIfCancellationRequested();
                Dispatcher.UIThread.Post(() =>
                {
                    foreach (var row in needStats)
                    {
                        if (this.statsCache.TryGetValue(row.Oid, out var stats)) row.DeliverStats(stats);
                    }
                });
            }
        }
        catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException)
        {
            // The tab was closed.
        }
    }

    private static CommitInfo Missing(Oid oid)
    {
        var nobody = new Signature("?", "", DateTimeOffset.UnixEpoch);
        return new CommitInfo(oid, [], nobody, nobody, "(missing)");
    }

    // ── Search ────────────────────────────────────────────────────────

    private HistoryGraph? indexGraph;
    private Dictionary<Oid, CommitSummary> index = [];
    private int searchVersion;

    partial void OnSearchQueryChanged(string value) => _ = this.SearchAsync(debounce: true);

    /// <summary>
    /// Filters the rows by id, subject, author and ref names. The old app
    /// searched only the 500 commits it had loaded; this searches the whole
    /// loaded history, through an index read once per history load.
    /// </summary>
    private async Task SearchAsync(bool debounce)
    {
        var version = ++this.searchVersion;
        var query = this.SearchQuery.Trim();
        if (query.Length == 0)
        {
            this.SearchPending = false;
            this.ShownQuery = "";
            this.DisplayRows = this.Rows;
            return;
        }
        if (debounce) await Task.Delay(150);
        if (version != this.searchVersion || this.graph is not { } graph) return;

        if (!ReferenceEquals(this.indexGraph, graph))
        {
            this.SearchPending = true;
            var revisions = graph.Tips.Keys.Concat(this.HeadOid is { } head ? [head.ToHex()] : []).ToList();
            var summaries = await this.Tab.ReadAsync("Search history", repo => repo.CommitSummariesAsync(revisions));
            if (summaries is not null && ReferenceEquals(graph, this.graph))
            {
                this.index = summaries.ToDictionary(c => c.Oid);
                this.indexGraph = graph;
            }
            if (version != this.searchVersion) return;
            this.SearchPending = false;
        }

        var rows = this.Rows;
        var index = this.index;
        var matches = await Task.Run(() => rows.Where(r => Matches(r, query, index, graph)).ToArray());
        if (version != this.searchVersion) return;
        this.ShownQuery = query;
        this.DisplayRows = matches;
    }

    private static bool Matches(CommitRow row, string query, Dictionary<Oid, CommitSummary> index, HistoryGraph graph)
    {
        if (row.Oid.ToHex().Contains(query, StringComparison.OrdinalIgnoreCase)) return true;
        if (index.TryGetValue(row.Oid, out var c)
            && (c.Summary.Contains(query, StringComparison.OrdinalIgnoreCase) || c.Author.Contains(query, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }
        return graph.RefsOf(row.Oid).Any(r => r.ShortName.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    private static int IndexOf(IReadOnlyList<CommitRow> rows, CommitRow row)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            if (rows[i] == row) return i;
        }
        return -1;
    }

    [RelayCommand]
    private void ClearSearch() => this.SearchQuery = "";

    // ── Selection and commit details ──────────────────────────────────

    /// <summary>Selects only <paramref name="row"/> and shows its details.</summary>
    public void Select(CommitRow row)
    {
        this.SetPicked([row]);
        this.anchorOid = row.Oid;
        this.ShowDetails(row);
    }

    /// <summary>
    /// A click on a row (old app's HistoryList): Ctrl (⌘) toggles it in the selection, Shift selects
    /// the range from the last clicked row (adding to the selection with Ctrl too). The details panel
    /// follows the clicked row. Several selected commits can be cherry-picked.
    /// </summary>
    public void Click(CommitRow row, bool ctrl, bool shift)
    {
        var rows = this.DisplayRows;
        if (shift && this.anchorOid is { } anchorOid && rows.FirstOrDefault(r => r.Oid == anchorOid) is { } anchor)
        {
            var (from, to) = (IndexOf(rows, anchor), IndexOf(rows, row));
            var range = rows.Skip(Math.Min(from, to)).Take(Math.Abs(to - from) + 1);
            this.SetPicked(ctrl ? [.. this.PickedRows(), .. range] : range);
            this.ShowDetails(row);
            return;
        }
        if (ctrl)
        {
            this.anchorOid = row.Oid;
            if (this.picked.Contains(row.Oid))
            {
                this.SetPicked(this.PickedRows().Where(r => r != row));
                if (row == this.Selected)
                {
                    if (this.PickedRows().LastOrDefault() is { } other) this.ShowDetails(other);
                    else this.ClearSelection();
                }
                return;
            }
            this.SetPicked([.. this.PickedRows(), row]);
            this.ShowDetails(row);
            return;
        }
        this.Select(row);
    }

    private readonly HashSet<Oid> picked = [];

    private Oid? anchorOid;

    private IEnumerable<CommitRow> PickedRows() => this.Rows.Where(r => this.picked.Contains(r.Oid));

    private void SetPicked(IEnumerable<CommitRow> rows)
    {
        var next = rows.ToList();
        foreach (var r in this.PickedRows()) r.IsSelected = false;
        this.picked.Clear();
        foreach (var r in next)
        {
            this.picked.Add(r.Oid);
            r.IsSelected = true;
        }
        this.OnPropertyChanged(nameof(this.PickedCount));
        this.OnPropertyChanged(nameof(this.HasPicked));
        this.OnPropertyChanged(nameof(this.CherryPickLabel));
    }

    private void ShowDetails(CommitRow row)
    {
        if (row == this.Selected) return;
        this.Selected = row;
        this.selectedOid = row.Oid;
        this.LoadDetails(row.Oid);
    }

    /// <summary>The selected commits (old app: the cherry-pick button shows for any selection).</summary>
    public int PickedCount => this.picked.Count;

    public bool HasPicked => this.picked.Count > 0;

    public string CherryPickLabel => $"Cherry-pick {this.PickedCount} {(this.PickedCount == 1 ? "commit" : "commits")}";

    /// <summary>The selected commits oldest first (the list is newest first), the order git picks them in.</summary>
    public IReadOnlyList<Oid> PickedOldestFirst() => [.. this.Rows.Where(r => this.picked.Contains(r.Oid)).Reverse().Select(r => r.Oid)];

    [RelayCommand]
    private async Task CherryPick()
    {
        var commits = this.PickedOldestFirst();
        if (commits.Count == 0) return;
        if (await this.Tab.CherryPickAsync(commits)) this.SetPicked([]);
    }

    /// <summary>Up/Down in the list.</summary>
    public CommitRow? Move(int delta)
    {
        var rows = this.DisplayRows;
        if (rows.Count == 0) return null;
        var current = this.Selected is { } s ? (this.IsSearching ? IndexOf(rows, s) : s.Row) : -1;
        var row = rows[current < 0 ? 0 : Math.Clamp(current + delta, 0, rows.Count - 1)];
        if (row != this.Selected) this.Select(row);
        return row;
    }

    private void ClearSelection()
    {
        if (this.Selected is { } previous) previous.IsSelected = false;
        this.SetPicked([]);
        this.Selected = null;
        this.selectedOid = null;
        this.Details.Clear();
    }

    private void LoadDetails(Oid commit) =>
        this.Details.Load(commit, "List commit files",
            async repo => new CommitFileChanges(await repo.ListCommitFilesAsync(commit), await repo.CommitFileStatsAsync(commit)),
            (repo, path) => repo.DiffCommitFileAsync(commit, path));

    // ── Actions ───────────────────────────────────────────────────────

    /// <summary>
    /// Checks out a commit to browse history. A commit that is a local
    /// branch's tip switches to that branch instead, as in the old app.
    /// </summary>
    [RelayCommand]
    private async Task Checkout(CommitRow row)
    {
        if (row.IsHead || this.graph is not { } graph) return;
        var branch = graph.RefsOf(row.Oid).FirstOrDefault(r => r.Kind == RefKind.LocalBranch);
        if (branch is not null)
        {
            await this.Tab.SwitchAsync($"Switch to {branch.ShortName}", branch.ShortName, (repo, ct) => repo.SwitchBranchAsync(branch.ShortName, ct));
            return;
        }
        var anchor = this.MainBranch is { } main ? main["refs/heads/".Length..] : null;
        // Set before: the history lays out the browsed commit (with the anchor in lane 0) before the run returns.
        var previous = this.Anchor;
        this.Anchor ??= anchor;
        if (!await this.Tab.RunAsync($"Check out {row.ShortHash}", (repo, ct) => repo.CheckoutCommitAsync(row.Oid, ct), blocking: true))
        {
            this.Anchor = previous;
        }
    }

    /// <summary>Leaves history browsing: back to the branch whose history was being explored.</summary>
    [RelayCommand]
    private async Task JumpToPresent()
    {
        var branch = this.MainBranch is { } main ? main["refs/heads/".Length..] : null;
        if (branch is null) return;
        await this.Tab.SwitchAsync($"Switch to {branch}", branch, (repo, ct) => repo.SwitchBranchAsync(branch, ct));
    }

    /// <summary>Reverts the row's commit on the current branch (old app: no confirmation; the revert is a new commit).</summary>
    [RelayCommand]
    private Task Revert(CommitRow row) => this.Tab.RevertAsync(row.Oid);

    [RelayCommand]
    private async Task UndoCommit(CommitRow row)
    {
        if (!row.CanUndo) return;
        await this.Tab.Staging.UndoLastCommitAsync();
        this.Tab.LeftTab = LeftPanelTab.Staging;
    }

    public void Dispose()
    {
        this.stop.Cancel();
        this.pending.Release();
    }
}
