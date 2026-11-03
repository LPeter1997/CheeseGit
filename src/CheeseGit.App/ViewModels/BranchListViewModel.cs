using CheeseGit.Core.Repository;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CheeseGit.App.ViewModels;

/// <summary>
/// A branch row with a merge button. Its tooltip predicts the merge (<c>git merge-tree</c>, in memory)
/// when it opens: one read per hover, not per row, since the dropdown can list hundreds of branches.
/// Rows are rebuilt on every snapshot, so a prediction never outlives the HEAD it was made against.
/// </summary>
public abstract partial class MergeableBranchRow(BranchListViewModel owner, string mergeSource) : ObservableObject
{
    /// <summary>One line under the tooltip's first: "No conflicts expected", "Conflicts expected in 2 files: …".</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MergeToolTip))]
    public partial string? Prediction { get; private set; }

    private bool predicting;

    protected BranchListViewModel Owner => owner;

    public string MergeToolTip => $"Merge {mergeSource} into the current branch" + (this.Prediction is { } p ? "\n" + p : "");

    [RelayCommand]
    private Task Merge() => owner.MergeAsync(mergeSource);

    public async Task PredictAsync()
    {
        if (this.predicting || this.Prediction is not null) return;
        this.predicting = true;
        this.Prediction = "Checking for conflicts…";
        var prediction = await owner.Tab.ReadAsync("Predict merge", repo => repo.PredictMergeAsync(mergeSource));
        this.Prediction = Describe(prediction);
    }

    internal static string? Describe(MergePrediction? prediction) => prediction switch
    {
        MergePrediction.UpToDate => "Already up to date: nothing to merge",
        MergePrediction.Clean => "No conflicts expected",
        MergePrediction.Conflicts { Files: var files } => $"Conflicts expected in {files.Count} {(files.Count == 1 ? "file" : "files")}: "
            + string.Join(", ", files.Take(3).Select(f => f.ToString())) + (files.Count > 3 ? $" and {files.Count - 3} more" : ""),
        _ => null, // git older than 2.38, or the branch went away: say nothing rather than guess.
    };
}

/// <summary>A local branch in the branch dropdown.</summary>
public sealed partial class LocalBranchRow(BranchListViewModel owner, BranchInfo info, GraphBranch? graph) : MergeableBranchRow(owner, info.Name)
{
    public BranchInfo Info { get; } = info;

    public string Name => this.Info.Name;

    public bool IsCurrent => this.Info.IsCurrent;

    public bool IsNotCurrent => !this.Info.IsCurrent;

    public string Date => CommitRow.RelativeDate(this.Info.LastCommitDate);

    /// <summary>The branch's eye toggle, when the loaded graph has it.</summary>
    public GraphBranch? Graph { get; } = graph;

    public bool HasGraph => this.Graph is not null;

    [RelayCommand]
    private Task Switch() => this.Owner.SwitchAsync(this);

    [RelayCommand]
    private Task Delete() => this.Owner.DeleteAsync(this);
}

/// <summary>A remote branch with no local branch of its name; clicking it creates the local branch.</summary>
public sealed partial class RemoteBranchRow(BranchListViewModel owner, RemoteBranchInfo info, GraphBranch? graph)
    : MergeableBranchRow(owner, $"{info.Remote}/{info.Name}")
{
    public RemoteBranchInfo Info { get; } = info;

    public string Name => this.Info.Name;

    public string Remote => this.Info.Remote;

    public string Date => CommitRow.RelativeDate(this.Info.LastCommitDate);

    public GraphBranch? Graph { get; } = graph;

    public bool HasGraph => this.Graph is not null;

    [RelayCommand]
    private Task Switch() => this.Owner.SwitchAsync(this);
}

/// <summary>A start point for a new branch (#60).</summary>
public sealed record BranchBase(string Label, string? StartPoint)
{
    public override string ToString() => this.Label;
}

/// <summary>
/// The branch dropdown (old app's BranchDropdown): find, switch to, create and delete branches, and
/// choose which ones the graph shows. The lists come from git when the dropdown opens (they carry
/// dates the snapshot does not have) and again when refs change while it is open.
/// </summary>
public sealed partial class BranchListViewModel(RepoTabViewModel tab) : ObservableObject
{
    private IReadOnlyList<BranchInfo> locals = [];
    private IReadOnlyList<RemoteBranchInfo> remotes = [];
    private int loadVersion;

    public RepoTabViewModel Tab { get; } = tab;

    /// <summary>Raised when an action leaves the dropdown (switching, creating, deleting).</summary>
    public event Action? CloseRequested;

    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    [ObservableProperty]
    public partial bool IsLoaded { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CreateLabel))]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    public partial string Query { get; set; } = "";

    /// <summary>Local rows, then a "Remote" header and the remote-only rows, filtered by <see cref="Query"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasItems), nameof(NoMatches), nameof(NoMatchesText))]
    public partial IReadOnlyList<object> Items { get; private set; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    public partial bool CanCreate { get; private set; }

    /// <summary>Where a new branch starts: the current branch (or checked-out commit) first, then every branch.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<BranchBase> Bases { get; private set; } = [];

    [ObservableProperty]
    public partial BranchBase? Base { get; set; }

    public bool HasItems => this.Items.Count > 0;

    public bool NoMatches => this.IsLoaded && this.Items.Count == 0 && this.Query.Trim().Length > 0;

    public string NoMatchesText => $"No branch matching “{this.Query.Trim()}”";

    public string CreateLabel => $"Create branch “{this.Query.Trim()}”";

    partial void OnIsOpenChanged(bool value)
    {
        if (!value) return;
        this.Query = "";
        this.UpdateBases();
        _ = this.LoadAsync();
    }

    partial void OnQueryChanged(string value) => this.Rebuild();

    /// <summary>Refs changed: reload if the dropdown is showing them.</summary>
    internal void OnSnapshot(SnapshotParts changed)
    {
        if ((changed & (SnapshotParts.Refs | SnapshotParts.Head)) == 0) return;
        if (this.IsOpen)
        {
            this.UpdateBases();
            _ = this.LoadAsync();
        }
        else
        {
            this.IsLoaded = false; // Stale: the next open shows the old list until the new one arrives.
        }
    }

    /// <summary>The graph's branches changed (its eye toggles are new objects after each layout).</summary>
    internal void OnGraphBranchesChanged()
    {
        if (this.IsOpen) this.Rebuild();
    }

    public async Task LoadAsync()
    {
        var version = ++this.loadVersion;
        var lists = await this.Tab.ReadAsync("List branches", async repo =>
            Tuple.Create(await repo.ListBranchesAsync(), await repo.ListRemoteBranchesAsync()));
        if (lists is null || version != this.loadVersion) return;
        (this.locals, this.remotes) = lists;
        this.IsLoaded = true;
        this.Rebuild();
    }

    private void Rebuild()
    {
        var query = this.Query.Trim();
        bool Matches(string name) => name.Contains(query, StringComparison.OrdinalIgnoreCase);
        var graph = this.Tab.History.Branches.ToDictionary(b => b.FullName, StringComparer.Ordinal);
        var items = new List<object>();
        foreach (var b in this.locals.Where(b => Matches(b.Name)))
        {
            items.Add(new LocalBranchRow(this, b, graph.GetValueOrDefault("refs/heads/" + b.Name)));
        }
        var remoteRows = this.remotes.Where(b => Matches(b.Name)).ToList();
        if (remoteRows.Count > 0)
        {
            items.Add(BranchListHeader.Remote);
            foreach (var b in remoteRows) items.Add(new RemoteBranchRow(this, b, graph.GetValueOrDefault($"refs/remotes/{b.Remote}/{b.Name}")));
        }
        this.Items = items;
        // Old app: offer creating unless a branch (local or remote) has exactly this name.
        this.CanCreate = query.Length > 0
            && !this.locals.Any(b => string.Equals(b.Name, query, StringComparison.OrdinalIgnoreCase))
            && !this.remotes.Any(b => string.Equals(b.Name, query, StringComparison.OrdinalIgnoreCase));
        this.OnPropertyChanged(nameof(this.NoMatches));
        this.OnPropertyChanged(nameof(this.NoMatchesText));
    }

    private void UpdateBases()
    {
        var snapshot = this.Tab.Snapshot;
        var head = snapshot?.Head;
        var current = head switch
        {
            null or { IsUnborn: true } => null,
            { BrowsingHistory: false, Branch: { } branch } => new BranchBase($"{branch} (current)", null),
            { Commit: { } commit } => new BranchBase($"{commit.ToShortHex()} (checked out)", null),
            _ => null,
        };
        var bases = new List<BranchBase>();
        if (current is not null) bases.Add(current);
        if (snapshot is not null)
        {
            var currentRef = head?.BrowsingHistory == false && head.Branch is { } b ? "refs/heads/" + b : null;
            bases.AddRange(snapshot.Refs
                .Where(r => r.Kind is RefKind.LocalBranch && r.FullName != currentRef)
                .OrderBy(r => r.ShortName, StringComparer.OrdinalIgnoreCase)
                .Select(r => new BranchBase(r.ShortName, r.FullName)));
            bases.AddRange(snapshot.Refs
                .Where(r => r.Kind is RefKind.RemoteBranch && !r.FullName.EndsWith("/HEAD", StringComparison.Ordinal))
                .OrderBy(r => r.ShortName, StringComparer.OrdinalIgnoreCase)
                .Select(r => new BranchBase(r.ShortName, r.FullName)));
        }
        var previous = this.Base?.StartPoint;
        this.Bases = bases;
        this.Base = bases.FirstOrDefault(b => b.StartPoint == previous && previous is not null) ?? bases.FirstOrDefault();
    }

    /// <summary>Enter in the search box: switch to the branch with exactly that name, else create it.</summary>
    [RelayCommand]
    private async Task Submit()
    {
        var query = this.Query.Trim();
        if (query.Length == 0) return;
        var exact = this.Items.FirstOrDefault(i => i switch
        {
            LocalBranchRow l => string.Equals(l.Name, query, StringComparison.OrdinalIgnoreCase),
            RemoteBranchRow r => string.Equals(r.Name, query, StringComparison.OrdinalIgnoreCase),
            _ => false,
        });
        switch (exact)
        {
            case LocalBranchRow l: await this.SwitchAsync(l); break;
            case RemoteBranchRow r: await this.SwitchAsync(r); break;
            default: if (this.CanCreate) await this.Create(); break;
        }
    }

    [RelayCommand(CanExecute = nameof(CanCreate))]
    private async Task Create()
    {
        var name = this.Query.Trim();
        var start = this.Base?.StartPoint;
        this.CloseRequested?.Invoke();
        // From the current branch the changes are what the new branch is for: no question.
        await this.Tab.SwitchAsync($"Create branch {name}", name, (repo, ct) => repo.CreateBranchAsync(name, start, ct), askAboutChanges: start is not null);
    }

    internal async Task SwitchAsync(LocalBranchRow row)
    {
        this.CloseRequested?.Invoke();
        // No shortcut for the current branch: the row and the snapshot can both lag a switch that
        // just happened, and switching to the branch git is on is a no-op anyway.
        await this.Tab.SwitchAsync($"Switch to {row.Name}", row.Name, (repo, ct) => repo.SwitchBranchAsync(row.Name, ct));
    }

    internal async Task SwitchAsync(RemoteBranchRow row)
    {
        this.CloseRequested?.Invoke();
        await this.Tab.SwitchAsync($"Check out {row.Remote}/{row.Name}", row.Name, (repo, ct) => repo.CheckoutRemoteBranchAsync(row.Remote, row.Name, ct));
    }

    internal async Task MergeAsync(string branch)
    {
        this.CloseRequested?.Invoke();
        if (this.Tab.BrowsingHistory)
        {
            this.Tab.Shell.ShowError("Cannot merge while browsing history. Jump back to a branch first.");
            return;
        }
        await this.Tab.MergeAsync(branch);
    }

    internal async Task DeleteAsync(LocalBranchRow row)
    {
        if (row.IsCurrent) return;
        this.CloseRequested?.Invoke();
        var dialog = new DeleteBranchDialogViewModel(this.Tab, row.Name);
        _ = dialog.LoadAsync();
        await this.Tab.Shell.ShowDialogAsync(dialog);
    }
}

/// <summary>Confirms deleting a branch, offering to delete it on its remote too (old app's DeleteBranchDialog).</summary>
public sealed partial class DeleteBranchDialogViewModel(RepoTabViewModel tab, string branch) : DialogViewModel
{
    public string Branch { get; } = branch;

    public string Title => $"Delete branch “{this.Branch}”?";

    /// <summary>Asking the remote whether it has the branch (ls-remote); the checkbox appears after.</summary>
    [ObservableProperty]
    public partial bool CheckingRemote { get; private set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDeleteRemote), nameof(DeleteRemoteLabel))]
    public partial BranchDeleteInfo? Info { get; private set; }

    public bool CanDeleteRemote => this.Info is { ExistsOnRemote: true, RemoteName: not null };

    public string DeleteRemoteLabel => $"Also delete on remote ({this.Info?.RemoteName})";

    [ObservableProperty]
    public partial bool DeleteRemote { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DeleteLabel))]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand))]
    public partial bool Busy { get; private set; }

    public string DeleteLabel => this.Busy ? "Deleting…" : "Delete";

    /// <summary>Commits on no other branch: deleting (forced) loses them from every branch.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUniqueCommits), nameof(UniqueCommitsWarning))]
    public partial int UniqueCommits { get; private set; }

    public bool HasUniqueCommits => this.UniqueCommits > 0;

    public string UniqueCommitsWarning => this.UniqueCommits == 1
        ? "1 commit on this branch is on no other branch. Deleting the branch loses it."
        : $"{this.UniqueCommits} commits on this branch are on no other branch. Deleting the branch loses them.";

    internal async Task LoadAsync()
    {
        var unique = tab.ReadAsync("Count unmerged commits", async repo => (object)await repo.UniqueCommitCountAsync(this.Branch));
        // A remote that cannot be reached leaves the checkbox out; deleting locally still works.
        var info = tab.ReadAsync("Check remote branch", repo => repo.BranchDeleteInfoAsync(this.Branch));
        if (await unique is int count) this.UniqueCommits = count;
        this.Info = await info;
        this.CheckingRemote = false;
    }

    private bool CanDelete() => !this.Busy;

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private async Task Delete()
    {
        this.Busy = true;
        this.CanClose = false;
        var info = this.DeleteRemote && this.CanDeleteRemote ? this.Info : null;
        // Forced, as in the old app: the dialog is the confirmation.
        var deleted = await tab.RunAsync($"Delete branch {this.Branch}", (repo, ct) => repo.DeleteBranchAsync(this.Branch, force: true, ct));
        if (deleted && info is { RemoteName: { } remote, RemoteBranchName: { } remoteBranch })
        {
            await tab.RunAsync($"Delete {remote}/{remoteBranch}", (repo, ct) => repo.DeleteRemoteBranchAsync(remote, remoteBranch, ct));
        }
        this.Busy = false;
        this.CanClose = true;
        this.Close(deleted);
    }
}
