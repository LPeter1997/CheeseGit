using System.Text.RegularExpressions;
using CheeseGit.Core.Git;
using CheeseGit.Core.Repository;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CheeseGit.App.ViewModels;

public enum RemoteAction
{
    Add,
    Publish,
    Push,
    Pull,
    Fetch,
}

/// <summary>A remote in the remote picker.</summary>
public sealed partial class RemoteRow(RemoteViewModel owner, RemoteInfo info, bool isActive) : ObservableObject
{
    public string Name => info.Name;

    public string Url => info.Url;

    public bool IsActive { get; } = isActive;

    /// <summary>The trash button was pressed once; it now asks to confirm in place (old app).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NotConfirming))]
    public partial bool ConfirmingRemove { get; set; }

    public bool NotConfirming => !this.ConfirmingRemove;

    [RelayCommand]
    private void Select() => owner.Select(this.Name);

    [RelayCommand]
    private void AskRemove()
    {
        foreach (var row in owner.Rows) row.ConfirmingRemove = false;
        this.ConfirmingRemove = true;
    }

    [RelayCommand]
    private void CancelRemove() => this.ConfirmingRemove = false;

    [RelayCommand]
    private Task Remove() => owner.RemoveAsync(this.Name);
}

/// <summary>
/// The remote button in the branch bar (old app's RemoteButton): one action that fits the current
/// branch's state against the chosen remote (publish, pull, push, else fetch), and a picker that
/// chooses the remote, removes remotes and adds new ones. The chosen remote also decides which
/// commits the history marks as local-only.
/// </summary>
public sealed partial class RemoteViewModel(RepoTabViewModel tab) : ObservableObject
{
    private CancellationTokenSource? running;
    private int statusVersion;

    public RepoTabViewModel Tab { get; } = tab;

    /// <summary>The chosen remote's name, or null without remotes.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Label), nameof(ToolTip))]
    public partial string? Active { get; private set; }

    /// <summary>The current branch against <see cref="Active"/>: null when it has no branch of that name there.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Action), nameof(Label), nameof(ToolTip), nameof(IsPublish), nameof(IsPush), nameof(IsPull), nameof(IsFetch), nameof(IsAdd))]
    public partial TrackingStatus? Tracking { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Action), nameof(Label), nameof(ToolTip), nameof(IsPublish), nameof(IsPush), nameof(IsPull), nameof(IsFetch), nameof(IsAdd))]
    public partial IReadOnlyList<RemoteRow> Rows { get; private set; } = [];

    /// <summary>A push, pull, fetch or publish is running; the button then cancels it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NotBusy), nameof(ToolTip), nameof(CanAct), nameof(Label), nameof(HasProgressValue))]
    public partial bool Busy { get; private set; }

    public bool NotBusy => !this.Busy;

    /// <summary>The running action's verb, for the label until git reports progress.</summary>
    private RemoteAction runningAction;

    /// <summary>The latest progress meter of the running action; null until git prints one.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Label), nameof(ToolTip), nameof(ProgressValue), nameof(HasProgressValue))]
    public partial GitProgress? Progress { get; private set; }

    /// <summary>The bar under the label: the current phase's percentage (phases restart it).</summary>
    public double ProgressValue => this.Progress?.Percent ?? 0;

    public bool HasProgressValue => this.Busy && this.Progress?.Percent is not null;

    public RemoteAction Action => this.Rows.Count == 0 ? RemoteAction.Add
        : this.Tracking is not { } t ? RemoteAction.Publish
        : t.Behind > 0 ? RemoteAction.Pull
        : t.Ahead > 0 ? RemoteAction.Push
        : RemoteAction.Fetch;

    public bool IsAdd => this.Action == RemoteAction.Add;

    public bool IsPublish => this.Action == RemoteAction.Publish;

    public bool IsPush => this.Action == RemoteAction.Push;

    public bool IsPull => this.Action == RemoteAction.Pull;

    public bool IsFetch => this.Action == RemoteAction.Fetch;

    public string Label => this.Busy ? this.BusyLabel : this.Action switch
    {
        RemoteAction.Add => "Add remote",
        RemoteAction.Publish => $"Publish to {this.Active}",
        RemoteAction.Push => $"Push {this.Tracking!.Ahead} to {this.Active}",
        RemoteAction.Pull => $"Pull {this.Tracking!.Behind} from {this.Active}",
        _ => $"Fetch {this.Active}",
    };

    private string BusyLabel => this.Progress switch
    {
        { Percent: { } percent } p => $"{p.Phase} {percent}%",
        { Done: { } done } p => $"{p.Phase} {done}",
        _ => this.runningAction switch
        {
            RemoteAction.Publish => $"Publishing to {this.Active}…",
            RemoteAction.Push => $"Pushing to {this.Active}…",
            RemoteAction.Pull => $"Pulling from {this.Active}…",
            _ => $"Fetching {this.Active}…",
        },
    };

    /// <summary>Remote actions need a branch; adding a remote does not.</summary>
    public bool CanAct => this.Busy || this.IsAdd || !this.Tab.BrowsingHistory;

    public string ToolTip =>
        this.Busy ? (this.Progress is { } progress ? $"{progress.Line}\nClick to cancel" : "Click to cancel")
        : this.IsAdd ? "Add a remote"
        : this.Tab.BrowsingHistory ? "Sync disabled while viewing history"
        : this.Tracking is { } t ? $"{t.Ahead} ahead, {t.Behind} behind {t.Upstream}"
        : $"The branch is not on {this.Active} yet";

    internal void OnSnapshot(SnapshotParts changed)
    {
        if ((changed & SnapshotParts.Remotes) != 0) this.UpdateRemotes();
        if ((changed & (SnapshotParts.Status | SnapshotParts.Head | SnapshotParts.Refs | SnapshotParts.Remotes)) != 0) _ = this.UpdateTrackingAsync();
        if ((changed & SnapshotParts.Head) != 0)
        {
            this.OnPropertyChanged(nameof(this.CanAct));
            this.OnPropertyChanged(nameof(this.ToolTip));
        }
    }

    private void UpdateRemotes()
    {
        var remotes = this.Tab.Snapshot?.Remotes ?? [];
        var active = remotes.Any(r => r.Name == this.Active) ? this.Active : HistoryViewModel.PickRemote(remotes);
        this.Rows = [.. remotes.Select(r => new RemoteRow(this, r, r.Name == active))];
        this.Active = active;
    }

    /// <summary>
    /// Ahead/behind from the status when the upstream is on the chosen remote; otherwise against
    /// the branch of the same name there (old app), which needs a read.
    /// </summary>
    private async Task UpdateTrackingAsync()
    {
        var version = ++this.statusVersion;
        var branch = this.Tab.Snapshot?.Status.Branch;
        if (this.Active is not { } remote || this.Tab.Snapshot?.Head.Branch is null)
        {
            this.Tracking = null;
            return;
        }
        if (branch is { Upstream: { } upstream, Ahead: { } ahead, Behind: { } behind } && upstream.StartsWith(remote + "/", StringComparison.Ordinal))
        {
            this.Tracking = new TrackingStatus(ahead, behind, upstream);
            return;
        }
        // Null both when the branch is not there and when the read failed: publishing then says why.
        var status = await this.Tab.ReadAsync<TrackingStatus>("Compare with remote", async repo => (await repo.RemoteBranchStatusAsync(remote))!);
        if (version == this.statusVersion) this.Tracking = status;
    }

    internal void Select(string name)
    {
        this.Active = name;
        this.Rows = [.. this.Rows.Select(r => new RemoteRow(this, new RemoteInfo(r.Name, r.Url), r.Name == name))];
        _ = this.UpdateTrackingAsync();
        this.Tab.History.OnRemoteChanged();
        this.PickerCloseRequested?.Invoke();
    }

    /// <summary>Snapshot mode (<c>CHEESEGIT_SNAPSHOT_DIALOG=remote-progress</c>): a fetch halfway through receiving.</summary>
    internal void ShowSampleProgress()
    {
        this.runningAction = RemoteAction.Fetch;
        this.Busy = true;
        this.Progress = GitProgress.Parse("Receiving objects:  45% (450/1000), 1.20 MiB | 600.00 KiB/s");
    }

    /// <summary>Raised when a picker action is done (select, add).</summary>
    public event Action? PickerCloseRequested;

    /// <summary>Concurrent, so the button stays enabled while it runs: the second click cancels.</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task Run()
    {
        if (this.Busy)
        {
            this.running?.Cancel();
            return;
        }
        if (this.IsAdd)
        {
            await this.AddRemote();
            return;
        }
        if (this.Active is not { } remote || !this.CanAct) return;
        await this.RunActionAsync(this.Action, remote);
    }

    private async Task RunActionAsync(RemoteAction action, string remote)
    {
        this.runningAction = action;
        var pulledIntoConflicts = false;
        var missing = 0;
        using var cts = this.running = new CancellationTokenSource();
        this.Progress = null;
        this.Busy = true;
        // Progress<T> posts to the UI thread. Lines that are not meters ("From …", ref updates)
        // would make the label flicker, so only meters show; the command log keeps the rest.
        var progress = new Progress<GitProgress>(p =>
        {
            if (this.running == cts && p.Done is not null) this.Progress = p;
        });
        try
        {
            var label = action switch
            {
                RemoteAction.Publish => $"Publish to {remote}",
                RemoteAction.Push => $"Push to {remote}",
                RemoteAction.Pull => $"Pull from {remote}",
                _ => $"Fetch {remote}",
            };
            var conflicts = false;
            var rejected = 0;
            await this.Tab.RunAsync(label, async (repo, ct) =>
            {
                switch (action)
                {
                    case RemoteAction.Publish: rejected = await repo.PublishAsync(remote, progress, ct); break;
                    case RemoteAction.Push: rejected = await repo.PushAsync(remote, progress, ct); break;
                    case RemoteAction.Pull: conflicts = await repo.PullAsync(remote, progress, ct); break;
                    default: await repo.FetchAsync(remote, progress, ct); break;
                }
            }, ct: cts.Token);
            if (conflicts) pulledIntoConflicts = true;
            missing = rejected;
        }
        catch (OperationCanceledException)
        {
            // Cancelled with the button: git was stopped, and the session refreshes anyway.
        }
        finally
        {
            this.running = null;
            this.Busy = false;
            this.Progress = null;
        }
        // The pull merged with conflicts: the merge is paused, as after a merge from the dropdown.
        if (pulledIntoConflicts) await this.Tab.OpenConflictsAfterAsync();
        // The remote moved since the last fetch. Its commits are fetched now; pulling them comes first.
        if (missing > 0)
        {
            var commits = missing == 1 ? "1 commit" : $"{missing} commits";
            this.Tab.Shell.ShowInfo(
                $"Not pushed: {remote} has {commits} that this branch does not have. Pull them first, then push.",
                "Pull", () => this.Busy ? Task.CompletedTask : this.RunActionAsync(RemoteAction.Pull, remote));
        }
    }

    [RelayCommand]
    private async Task AddRemote()
    {
        this.PickerCloseRequested?.Invoke();
        var dialog = new AddRemoteDialogViewModel(this.Tab, [.. this.Rows.Select(r => r.Name)]);
        if (await this.Tab.Shell.ShowDialogAsync(dialog)) this.Select(dialog.Name.Trim());
    }

    internal async Task RemoveAsync(string name)
    {
        await this.Tab.RunAsync($"Remove remote {name}", (repo, ct) => repo.RemoveRemoteAsync(name, ct));
    }
}

/// <summary>Adds a remote and checks that it answers (old app's AddRemoteDialog).</summary>
public sealed partial class AddRemoteDialogViewModel : DialogViewModel
{
    private readonly RepoTabViewModel tab;
    private readonly IReadOnlyList<string> existing;
    private bool nameEdited;
    private bool settingName;

    public AddRemoteDialogViewModel(RepoTabViewModel tab, IReadOnlyList<string> existing)
    {
        this.tab = tab;
        this.existing = existing;
        // The first remote is "origin", as git clone names it.
        if (existing.Count == 0)
        {
            this.Name = "origin";
            this.nameEdited = true;
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NameError), nameof(HasNameError))]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    public partial string Url { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NameError), nameof(HasNameError))]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    public partial string Name { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? Error { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AddLabel))]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    public partial bool Busy { get; private set; }

    public bool HasError => this.Error is not null;

    public string AddLabel => this.Busy ? "Adding…" : "Add";

    public string? NameError
    {
        get
        {
            var name = this.Name.Trim();
            if (name.Length == 0) return null;
            if (this.existing.Contains(name)) return $"A remote named “{name}” already exists.";
            return ValidName().IsMatch(name) ? null : "Use letters, digits, '.', '_' and '-'.";
        }
    }

    public bool HasNameError => this.NameError is not null;

    partial void OnUrlChanged(string value)
    {
        this.Error = null;
        if (this.nameEdited || SuggestName(value.Trim()) is not { Length: > 0 } suggested) return;
        var candidate = suggested;
        for (var n = 2; this.existing.Contains(candidate); n++) candidate = suggested + n;
        this.settingName = true;
        this.Name = candidate;
        this.settingName = false;
    }

    partial void OnNameChanged(string value)
    {
        this.Error = null;
        if (!this.settingName) this.nameEdited = true;
    }

    /// <summary>The host's main label: git@github.com:… → "github", https://gitlab.com/… → "gitlab" (old app).</summary>
    internal static string SuggestName(string url)
    {
        var host = ScpHost().Match(url) is { Success: true } scp ? scp.Groups[1].Value
            : UrlHost().Match(url) is { Success: true } u ? u.Groups[1].Value
            : null;
        if (host is null) return "";
        var parts = host.Split(':')[0].Split('.');
        return parts.Length >= 2 ? parts[^2] : parts[0];
    }

    private bool CanAdd() => !this.Busy && this.Url.Trim().Length > 0 && this.Name.Trim().Length > 0 && this.NameError is null;

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private async Task Add()
    {
        if (this.tab.Session is not { } session) return;
        var name = this.Name.Trim();
        var url = this.Url.Trim();
        this.Busy = true;
        this.Error = null;
        var started = DateTimeOffset.Now;
        try
        {
            // Not tab.RunAsync: a failure belongs in the dialog, where the user can fix the URL.
            await session.RunAsync($"Add remote {name}", (repo, ct) => repo.AddRemoteAsync(name, url, ct), SnapshotParts.Remotes | SnapshotParts.Refs);
            this.Busy = false;
            this.Close(true);
        }
        catch (Exception e) when (e is GitException or IOException)
        {
            (this.tab.Shell as MainViewModel)?.ForgetAnswersUsedSince(started);
            this.Error = e.Message;
            this.Busy = false;
        }
    }

    [GeneratedRegex("^[a-zA-Z0-9._-]+$")]
    private static partial Regex ValidName();

    [GeneratedRegex("^[^@/]+@([^:/]+):")]
    private static partial Regex ScpHost();

    [GeneratedRegex("://(?:[^@/]+@)?([^/]+)")]
    private static partial Regex UrlHost();
}
