using System.Text.RegularExpressions;
using Avalonia.Threading;
using CheeseGit.Core.Git;
using CheeseGit.Core.Repository;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CheeseGit.App.ViewModels;

/// <summary>One stash entry in the Stash tab (old app's StashRow).</summary>
public sealed partial class StashRow(StashViewModel owner, StashEntry entry) : ObservableObject
{
    public StashEntry Entry { get; } = entry;

    public Oid Oid => this.Entry.Oid;

    /// <summary>The message without git's "On branch:" prefix (old app's formatStashMessage).</summary>
    public string Title { get; } = StashViewModel.FormatMessage(entry.Message).Title;

    /// <summary>"On main", "WIP on feature/x"; null when the message has no such prefix.</summary>
    public string? Context { get; } = StashViewModel.FormatMessage(entry.Message).Context is { } c ? $"({c})" : null;

    public bool HasContext => this.Context is not null;

    public string Author => this.Entry.Author;

    public string Date => CommitRow.RelativeDate(this.Entry.When);

    public string ShortHash => this.Entry.Oid.ToShortHex();

    public string ToolTip => $"{this.Entry.Message}\n{this.Entry.Author}\n{this.Entry.When.LocalDateTime:yyyy-MM-dd HH:mm}";

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    [ObservableProperty]
    public partial bool HashCopied { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AdditionsText), nameof(DeletionsText), nameof(HasStats))]
    public partial (int Additions, int Deletions)? Stats { get; set; }

    public string AdditionsText => this.Stats is { Additions: > 0 } s ? "+" + DisplayText.Count(s.Additions) : "";

    public string DeletionsText => this.Stats is { Deletions: > 0 } s ? "−" + DisplayText.Count(s.Deletions) : "";

    public bool HasStats => this.Stats is { } s && (s.Additions > 0 || s.Deletions > 0);

    [RelayCommand]
    private Task Apply() => owner.ApplyAsync(this, pop: false);

    [RelayCommand]
    private Task Pop() => owner.ApplyAsync(this, pop: true);

    [RelayCommand]
    private Task Drop() => owner.DropAsync(this);

    [RelayCommand]
    private async Task CopyHash()
    {
        await owner.Tab.Shell.SetClipboardTextAsync(this.Oid.ToHex());
        this.HashCopied = true;
        DispatcherTimer.RunOnce(() => this.HashCopied = false, TimeSpan.FromSeconds(1.5));
    }
}

/// <summary>
/// The Stash tab (old app's StashList and StashDiffPanel): the stash entries with apply, pop and
/// drop, and the selected entry's files and diffs on the right. Entries come from the snapshot;
/// they are tracked by commit id, since an entry's index moves whenever something is stashed.
/// </summary>
public sealed partial class StashViewModel : ObservableObject
{
    private readonly Dictionary<Oid, (int, int)> statsCache = [];

    public StashViewModel(RepoTabViewModel tab, Services.DiffViewMode viewMode)
    {
        this.Tab = tab;
        this.Diff = new DiffViewModel(tab, viewMode);
        this.Details = new ChangeDetailsViewModel(tab, this.Diff, "Select a stash to view its changes.");
    }

    public RepoTabViewModel Tab { get; }

    public DiffViewModel Diff { get; }

    public ChangeDetailsViewModel Details { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial IReadOnlyList<StashRow> Rows { get; private set; } = [];

    public bool IsEmpty => this.Rows.Count == 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ContextLabel))]
    public partial StashRow? Selected { get; private set; }

    /// <summary>"stash@{0} - message" above the diff, like the history's commit label.</summary>
    public string? ContextLabel => this.Selected is { } row ? $"{row.Entry.Ref} - {row.Title}" : null;

    internal void OnSnapshot(SnapshotParts changed)
    {
        if ((changed & SnapshotParts.Stashes) == 0) return;
        var entries = this.Tab.Snapshot?.Stashes ?? [];
        var selected = this.Selected?.Oid;
        var rows = entries.Select(e => new StashRow(this, e)).ToList();
        foreach (var row in rows)
        {
            if (this.statsCache.TryGetValue(row.Oid, out var stats)) row.Stats = stats;
        }
        this.Rows = rows;
        // The selected entry keeps its selection at its new index; a dropped one clears it.
        if (selected is { } oid && rows.FirstOrDefault(r => r.Oid == oid) is { } still)
        {
            still.IsSelected = true;
            this.Selected = still;
        }
        else
        {
            this.Selected = null;
            this.Details.Clear();
        }
        _ = this.LoadStatsAsync(rows);
    }

    /// <summary>Each entry's total +/− (old app); one read per entry not seen before.</summary>
    private async Task LoadStatsAsync(IReadOnlyList<StashRow> rows)
    {
        foreach (var row in rows.Where(r => r.Stats is null))
        {
            var stats = await this.Tab.ReadAsync("Read stash stats", repo => repo.StashFileStatsAsync(row.Oid));
            if (stats is null) continue;
            var totals = (stats.Sum(s => s.Additions), stats.Sum(s => s.Deletions));
            this.statsCache[row.Oid] = totals;
            row.Stats = totals;
        }
    }

    public void Select(StashRow row)
    {
        if (this.Selected is { } previous) previous.IsSelected = false;
        row.IsSelected = true;
        this.Selected = row;
        var oid = row.Oid;
        this.Details.Load(oid, "List stash files",
            async repo => new CommitFileChanges(await repo.ListStashFilesAsync(oid), await repo.StashFileStatsAsync(oid)),
            (repo, path) => repo.DiffStashFileAsync(oid, path));
    }

    /// <summary>Up/Down in the list.</summary>
    public StashRow? Move(int delta)
    {
        if (this.Rows.Count == 0) return null;
        var current = this.Selected is { } s ? this.Rows.ToList().IndexOf(s) : -1;
        var row = this.Rows[current < 0 ? 0 : Math.Clamp(current + delta, 0, this.Rows.Count - 1)];
        if (row != this.Selected) this.Select(row);
        return row;
    }

    internal async Task ApplyAsync(StashRow row, bool pop)
    {
        // Old app: both ask first.
        var question = pop ? "Apply and remove this stash?" : "Apply this stash without removing it?";
        if (!await this.Tab.Shell.ConfirmAsync(question, pop ? "Pop" : "Apply", destructive: false)) return;
        var oid = row.Oid;
        var result = await this.Tab.RunAsync(pop ? "Pop stash" : "Apply stash", async (repo, ct) =>
        {
            var index = await IndexOfAsync(repo, oid, ct);
            return (object)(pop ? await repo.StashPopAsync(index, ct) : await repo.StashApplyAsync(index, ct));
        }, blocking: true);
        if (result is StashApplyResult.Conflicts)
        {
            this.Tab.Shell.ShowInfo("The stash was applied with conflicts and kept. Resolve them in the staging list.");
        }
    }

    internal async Task DropAsync(StashRow row)
    {
        if (!await this.Tab.Shell.ConfirmAsync("Delete this stash? This cannot be undone.", "Delete")) return;
        var oid = row.Oid;
        await this.Tab.RunAsync("Drop stash", async (repo, ct) => await repo.StashDropAsync(await IndexOfAsync(repo, oid, ct), ct), SnapshotParts.Stashes);
    }

    /// <summary>
    /// The entry's current index, read just before acting on it: the snapshot's can be stale
    /// (a stash made in a terminal moves every entry), and dropping by a stale index drops the
    /// wrong one.
    /// </summary>
    private static async Task<int> IndexOfAsync(GitRepository repo, Oid oid, CancellationToken ct) =>
        (await repo.ListStashesAsync(ct)).FirstOrDefault(e => e.Oid == oid)?.Index
            ?? throw new GitOperationException(GitErrorCode.NotFound, "This stash entry no longer exists.");

    /// <summary>"On main: fix" → ("fix", "On main"); "WIP on x: abc123 msg" → ("abc123 msg", "WIP on x").</summary>
    public static (string Title, string? Context) FormatMessage(string message)
    {
        var trimmed = message.Trim();
        var match = StashPrefix().Match(trimmed);
        if (!match.Success || match.Groups[3].Value.Trim().Length == 0) return (trimmed, null);
        return (match.Groups[3].Value.Trim(), $"{match.Groups[1].Value} {match.Groups[2].Value.Trim()}");
    }

    [GeneratedRegex(@"^(WIP on|On)\s+([^:]+):\s*(.*)$")]
    private static partial Regex StashPrefix();
}
