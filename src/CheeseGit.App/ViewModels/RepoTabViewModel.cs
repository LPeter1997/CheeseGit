using Avalonia.Threading;
using CheeseGit.Core.Git;
using CheeseGit.Core.Repository;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CheeseGit.App.ViewModels;

public enum LeftPanelTab
{
    Staging,
    History,
    Stash,
}

/// <summary>
/// One open repository. Owns its <see cref="RepositorySession"/> and projects
/// the session's snapshots onto the UI thread; it never keeps a second copy of
/// the state to patch (docs/architecture.md, State and refresh), and every mutation goes through
/// <see cref="RunAsync"/>, which queues it on the session.
/// </summary>
public sealed partial class RepoTabViewModel : ObservableObject, IDisposable
{
    private readonly MainViewModel shell;
    private bool disposed;

    public RepoTabViewModel(string path, MainViewModel shell)
    {
        this.Path = path;
        this.shell = shell;
        this.Name = NameOf(path);
        this.Diff = new DiffViewModel(this, shell.Settings.DiffViewMode);
        this.Staging = new StagingViewModel(this);
        this.History = new HistoryViewModel(this, shell.Settings.DiffViewMode);
        this.BranchList = new BranchListViewModel(this);
        this.Remote = new RemoteViewModel(this);
        this.Stash = new StashViewModel(this, shell.Settings.DiffViewMode);
        this.History.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(HistoryViewModel.Branches)) this.BranchList.OnGraphBranchesChanged();
        };
    }

    /// <summary>The working tree's root; a folder inside it becomes the root once the repository is open.</summary>
    [ObservableProperty]
    public partial string Path { get; private set; }

    [ObservableProperty]
    public partial string Name { get; private set; }

    private static string NameOf(string path) =>
        System.IO.Path.GetFileName(path.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));

    public IAppShell Shell => this.shell;

    /// <summary>The window's view model, for settings and the merge-tool registry.</summary>
    internal MainViewModel MainShell => this.shell;

    /// <summary>Records that this repository was offered a commit-graph; false if it already was.</summary>
    internal bool MarkCommitGraphOffered()
    {
        var offered = this.shell.Settings.CommitGraphOffered;
        if (offered.Contains(this.Path)) return false;
        offered.Add(this.Path);
        this.shell.Store.Save();
        return true;
    }

    public StagingViewModel Staging { get; }

    public DiffViewModel Diff { get; }

    public HistoryViewModel History { get; }

    /// <summary>The branch dropdown.</summary>
    public BranchListViewModel BranchList { get; }

    /// <summary>The remote button and the chosen remote.</summary>
    public RemoteViewModel Remote { get; }

    /// <summary>The diff the toolbar and Ctrl+F act on: the staging diff or the history's.</summary>
    public DiffViewModel ActiveDiff => this.IsHistoryTab ? this.History.Diff : this.IsStashTab ? this.Stash.Diff : this.Diff;

    /// <summary>The Stash tab: entries, and the selected one's files and diffs.</summary>
    public StashViewModel Stash { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    public partial RepositorySession? Session { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLoaded), nameof(BranchText), nameof(BrowsingHistory), nameof(HeadDetail),
        nameof(OperationBanner), nameof(HasOperation), nameof(CanResolveOperation), nameof(IsRebasing), nameof(OperationName), nameof(StashCount), nameof(HasStashes), nameof(StashTabLabel))]
    public partial RepoSnapshot? Snapshot { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOpening), nameof(StatusText))]
    public partial string? OpenError { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStagingTab), nameof(IsHistoryTab), nameof(IsStashTab), nameof(ActiveDiff))]
    public partial LeftPanelTab LeftTab { get; set; } = LeftPanelTab.Staging;

    partial void OnLeftTabChanged(LeftPanelTab value)
    {
        if (value == LeftPanelTab.History) this.History.EnsureLoaded();
    }

    /// <summary>Label of the mutation that is running, for the status bar.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    public partial string? CurrentOperation { get; private set; }

    public string StatusText =>
        this.CurrentOperation is { } op ? op + "…"
        : this.OpenError is not null ? "Could not open the repository"
        : this.Session is not { } session ? "Opening…"
        : $"Ready · {(session.IsWatching ? "watching for changes" : "polling for changes")} · git {session.Repository.Version}";

    /// <summary>
    /// The operation that rewrites the working tree and refs (a switch, a merge, …) while it runs and until the
    /// views have caught up with it. On a large repository that takes seconds, and clicks meanwhile hit a stale
    /// graph and branch list, so the view takes no input until it is null again.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBlocked), nameof(BlockingText))]
    public partial string? BlockingOperation { get; private set; }

    public bool IsBlocked => this.BlockingOperation is not null;

    public string BlockingText => this.BlockingOperation is { } op ? op + "…" : "";

    /// <summary>
    /// Whether the dimming and the spinner show. Input is refused at once, but they wait
    /// <see cref="BlockingOverlayDelay"/>, so a switch in a small repository does not flash.
    /// </summary>
    [ObservableProperty]
    public partial bool ShowBlockingOverlay { get; private set; }

    internal static TimeSpan BlockingOverlayDelay { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>Snapshot mode: the overlay as a long switch shows it.</summary>
    internal void ShowSampleBlocking()
    {
        this.BlockingOperation = "Switch to feature/large-refactor";
        this.ShowBlockingOverlay = true;
    }

    private int blockers;
    private DispatcherTimer? blockingTimer;

    [ObservableProperty]
    public partial bool IsActive { get; set; }

    /// <summary>Shift turns stage and unstage actions into discard actions (old app behaviour).</summary>
    [ObservableProperty]
    public partial bool ShiftHeld { get; set; }

    [ObservableProperty]
    public partial bool BranchNameCopied { get; private set; }

    public bool IsLoaded => this.Snapshot is not null;

    public bool IsOpening => this.Snapshot is null && this.OpenError is null;

    public bool IsStagingTab => this.LeftTab == LeftPanelTab.Staging;

    public bool IsHistoryTab => this.LeftTab == LeftPanelTab.History;

    public bool IsStashTab => this.LeftTab == LeftPanelTab.Stash;

    public bool BrowsingHistory => this.Snapshot?.Head.BrowsingHistory ?? false;

    public bool IsUnborn => this.Snapshot?.Head.IsUnborn ?? false;

    /// <summary>The branch name, or what HEAD is when there is no branch.</summary>
    public string BranchText => this.Snapshot?.Head switch
    {
        null => "…",
        // A rebase detaches HEAD; the branch being rebased is what the user is on.
        _ when this.Snapshot.Operation is { Kind: OperationKind.Rebase, Incoming.Length: > 0 } rebase => rebase.Incoming,
        { Branch: { } branch } => branch,
        { Commit: { } commit } => commit.ToShortHex(),
        _ => "HEAD",
    };

    public string? HeadDetail => this.Snapshot?.Head switch
    {
        _ when this.IsRebasing => "(rebasing)",
        { BrowsingHistory: true } => "(history)",
        { IsUnborn: true } => "(no commits yet)",
        _ => null,
    };


    public int StashCount => this.Snapshot?.Stashes.Count ?? 0;

    public bool HasStashes => this.StashCount > 0;

    public string StashTabLabel => $"Stash ({this.StashCount})";

    /// <summary>A paused merge, revert, cherry-pick or rebase (old app's merge-in-progress banner).</summary>
    public string? OperationBanner => this.Snapshot?.Operation switch
    {
        null or { Kind: OperationKind.None } => null,
        var op => $"{NameOf(op.Kind)} in progress" + (op.Incoming.Length > 0 ? $" ({op.Incoming})" : "")
            + (op.ConflictCount > 0 ? $": {op.ConflictCount} conflicted {(op.ConflictCount == 1 ? "file" : "files")}"
                : op.Kind == OperationKind.Rebase ? "" : ": no conflicts left"),
    };

    public bool HasOperation => this.OperationBanner is not null;

    /// <summary>The conflict dialog handles merges, reverts and cherry-picks; a rebase only gets the banner.</summary>
    public bool CanResolveOperation => this.Snapshot?.Operation.Kind is OperationKind.Merge or OperationKind.Revert or OperationKind.CherryPick;

    /// <summary>A paused rebase (a pull with pull.rebase, or one started in a terminal): the banner continues or aborts it.</summary>
    public bool IsRebasing => this.Snapshot?.Operation.Kind == OperationKind.Rebase;

    [RelayCommand]
    private async Task ContinueRebase()
    {
        if (this.Snapshot?.Operation is not { Kind: OperationKind.Rebase } op) return;
        if (op.ConflictCount > 0)
        {
            this.shell.ShowError($"{op.ConflictCount} conflicted {(op.ConflictCount == 1 ? "file needs" : "files need")} resolving first: edit {(op.ConflictCount == 1 ? "it" : "them")} and stage the result, then continue.");
            return;
        }
        await this.RunAsync("Continue rebase", (repo, ct) => repo.RebaseContinueAsync(ct), blocking: true);
    }

    [RelayCommand]
    private async Task AbortRebase()
    {
        if (!this.IsRebasing) return;
        var branch = this.Snapshot?.Operation.Incoming is { Length: > 0 } b ? b : "the branch";
        if (!await this.shell.ConfirmAsync($"Abort the rebase? {branch} goes back to where it was before the rebase started.", "Abort rebase")) return;
        await this.RunAsync("Abort rebase", (repo, ct) => repo.RebaseAbortAsync(ct), blocking: true);
    }

    /// <summary>"Merge", "Revert", "Cherry-pick" or "Rebase" for the paused operation; empty when there is none.</summary>
    public string OperationName => this.Snapshot?.Operation is { Kind: not OperationKind.None } op ? NameOf(op.Kind) : "";

    private static string NameOf(OperationKind kind) => kind switch
    {
        OperationKind.CherryPick => "Cherry-pick",
        _ => kind.ToString(),
    };

    private ConflictDialogViewModel? conflicts;

    /// <summary>Opens the conflict dialog for the paused operation (the banner's button, or after a merge or revert stops).</summary>
    [RelayCommand]
    public async Task ResolveConflicts()
    {
        if (this.conflicts is not null || this.Snapshot?.Operation is not { } op || !this.CanResolveOperation) return;
        var dialog = this.conflicts = new ConflictDialogViewModel(this, op);
        try
        {
            var shown = this.shell.ShowDialogAsync(dialog);
            await dialog.LoadAsync();
            await shown;
        }
        finally
        {
            this.conflicts = null;
        }
    }

    /// <summary>Merges <paramref name="branch"/> into the current branch; conflicts open the conflict dialog.</summary>
    public async Task MergeAsync(string branch)
    {
        var result = await this.RunAsync($"Merge {branch}", (repo, ct) => repo.MergeAsync(branch, ct), blocking: true);
        switch (result)
        {
            case MergeResult.Success { CommitsMerged: var n }:
                this.shell.ShowInfo($"Merged {n} {(n == 1 ? "commit" : "commits")} from {branch}");
                break;
            case MergeResult.AlreadyUpToDate:
                this.shell.ShowInfo($"Already up to date: nothing to merge from {branch}");
                break;
            case MergeResult.Conflict:
                await this.OpenConflictsAfterAsync();
                break;
        }
    }

    /// <summary>Reverts a commit on the current branch; conflicts open the conflict dialog.</summary>
    public async Task RevertAsync(Oid commit)
    {
        var result = await this.RunAsync($"Revert {commit.ToShortHex()}", (repo, ct) => repo.RevertAsync(commit, ct), blocking: true);
        if (result is RevertResult.Conflict) await this.OpenConflictsAfterAsync();
    }

    /// <summary>
    /// Asks for the target branch, switches to it (or creates it at HEAD) and picks
    /// <paramref name="commits"/> (oldest first); conflicts open the conflict dialog. True once the
    /// picks ran (paused on conflicts included), so the history drops its selection.
    /// </summary>
    public async Task<bool> CherryPickAsync(IReadOnlyList<Oid> commits)
    {
        var picker = new CherryPickDialogViewModel(commits);
        var shown = this.MainShell.ShowDialogAsync(picker);
        await picker.LoadAsync(this);
        if (!await shown || picker.Choice is not { } choice) return false;
        var count = commits.Count == 1 ? "1 commit" : $"{commits.Count} commits";
        var result = await this.RunAsync($"Cherry-pick {count} to {choice.Branch}", (repo, ct) => repo.CherryPickAsync(commits, choice.Branch, choice.Create, ct), blocking: true);
        switch (result)
        {
            case CherryPickResult.Success { Picked: var picked }:
                var skipped = commits.Count - picked;
                this.MainShell.ShowInfo($"Cherry-picked {count} to {choice.Branch}" + (skipped > 0 ? $" ({skipped} already there)" : ""));
                return true;
            case CherryPickResult.Conflict:
                await this.OpenConflictsAfterAsync();
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// The session refreshed before the mutation returned, but its snapshot event is still on its
    /// way to this thread: take the snapshot now so the dialog sees the paused operation.
    /// </summary>
    internal async Task OpenConflictsAfterAsync()
    {
        if (this.Session is not { } session) return;
        this.Apply(session.Snapshot);
        await this.ResolveConflicts();
    }

    public async Task OpenAsync()
    {
        try
        {
            var session = await Task.Run(async () =>
            {
                using var scope = GitRunner.BeginOperation("Open repository", background: true);
                try
                {
                    return await RepositorySession.OpenAsync(this.Path, git: this.shell.Git);
                }
                catch (Exception e)
                {
                    scope.Fail(e);
                    throw;
                }
            });
            if (this.disposed)
            {
                session.Dispose();
                return;
            }
            this.Session = session;
            // Opened from a folder inside the working tree: the tab is the repository, named after its root.
            if (session.Repository.Root.TrimEnd(System.IO.Path.DirectorySeparatorChar) is var root && root != this.Path.TrimEnd(System.IO.Path.DirectorySeparatorChar))
            {
                this.Path = root;
                this.Name = NameOf(root);
            }
            // Events arrive on background threads in publication order, and
            // posts keep that order.
            session.SnapshotChanged += change => Dispatcher.UIThread.Post(() => this.Apply(change.Snapshot));
            session.Refreshed += parts => Dispatcher.UIThread.Post(() => this.OnRefreshed(parts));
            session.CurrentOperationChanged += label => Dispatcher.UIThread.Post(() => this.CurrentOperation = label);
            session.RefreshFailed += e => Dispatcher.UIThread.Post(() => this.shell.ShowError($"{this.Name}: {e.Message}"));
            this.Apply(session.Snapshot);
            this.OnRefreshed(SnapshotParts.All);
            await this.CheckLfsAsync();
        }
        catch (Exception e) when (e is GitException or IOException or UnauthorizedAccessException)
        {
            this.OpenError = e is GitNotFoundException ? "Git was not found. Install git 2.34 or newer and make sure it is on PATH." : e.Message;
        }
    }

    private void Apply(RepoSnapshot snapshot)
    {
        if (this.disposed || (this.Snapshot is { } current && snapshot.Version < current.Version)) return;
        var previous = this.Snapshot;
        this.Snapshot = snapshot;
        this.Staging.Apply(snapshot, previous);
        var changed = previous is null ? SnapshotParts.All : snapshot.DifferencesFrom(previous);
        this.Remote.OnSnapshot(changed); // First: the history reads the chosen remote.
        this.History.OnSnapshot(changed);
        this.BranchList.OnSnapshot(changed);
        this.Stash.OnSnapshot(changed);
        if (!this.HasStashes && this.LeftTab == LeftPanelTab.Stash) this.LeftTab = LeftPanelTab.Staging;
        // Finished or aborted elsewhere (a terminal): the dialog has nothing left to do.
        if (this.conflicts is { Busy: false } dialog && !this.CanResolveOperation) dialog.Cancel();
    }

    private void OnRefreshed(SnapshotParts parts)
    {
        if (this.disposed) return;
        if ((parts & SnapshotParts.Status) != 0)
        {
            this.Staging.ReloadStats();
            this.Diff.Reload();
            // Editing a conflicted file does not change its status line, so the dialog re-reads its markers on every refresh.
            if (this.conflicts is { } dialog) _ = dialog.RefreshAsync();
        }
    }

    /// <summary>Re-reads the status (window focus: changes made while away when watching is off).</summary>
    public void RefreshOnFocus()
    {
        this.Session?.Invalidate(SnapshotParts.Status);
        // Git LFS may have been installed or set up in a terminal meanwhile.
        if (this.HasLfsBanner) _ = this.CheckLfsAsync();
    }

    // ── Git LFS ───────────────────────────────────────────────────────

    /// <summary>Whether the repository uses LFS and git-lfs is ready for it; read when the tab opens.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LfsBanner), nameof(HasLfsBanner), nameof(LfsNotInstalled), nameof(LfsCanSetUp))]
    public partial LfsSetup LfsSetup { get; private set; } = LfsSetup.NotUsed;

    /// <summary>"Not now" on the banner: hidden until the tab is opened again.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LfsBanner), nameof(HasLfsBanner))]
    public partial bool LfsBannerDismissed { get; private set; }

    /// <summary>git-lfs runs, so files can be tracked with it (the diff panel's offer).</summary>
    internal bool LfsInstalled { get; private set; }

    internal bool CanTrackWithLfs => this.LfsInstalled && this.LfsSetup is LfsSetup.Ready or LfsSetup.NotUsed;

    public string? LfsBanner => this.LfsBannerDismissed ? null : this.LfsSetup switch
    {
        LfsSetup.NotInstalled => "This repository uses Git LFS, which is not installed. Images and other large files show as small pointer files, and new ones would be committed without LFS.",
        LfsSetup.NotConfigured => "Git LFS is not set up for this repository. Checkouts leave pointer files in place of large files, and pushes can leave the large files behind.",
        _ => null,
    };

    public bool HasLfsBanner => this.LfsBanner is not null;

    public bool LfsNotInstalled => this.LfsSetup == LfsSetup.NotInstalled;

    public bool LfsCanSetUp => this.LfsSetup == LfsSetup.NotConfigured;

    internal long LfsAutoDownloadBytes => (long)Math.Max(0, this.shell.Settings.LfsAutoDownloadMegabytes) << 20;

    /// <summary>
    /// Downloads that failed, by object id, with git-lfs's message. The diff reloads on every refresh;
    /// without this an unreachable server would be asked again each time. Download retries.
    /// </summary>
    private readonly Dictionary<string, string> lfsFailures = new(StringComparer.Ordinal);

    internal string? LfsDownloadFailure(string oid) => this.lfsFailures.GetValueOrDefault(oid);

    internal void ForgetLfsDownloadFailure(string oid) => this.lfsFailures.Remove(oid);

    /// <summary>Downloads an LFS object; null (and the failure remembered) when it cannot be.</summary>
    internal async Task<FileContent?> DownloadLfsAsync(LfsPointer pointer, long maxBytes, bool background)
    {
        if (this.Session is not { } session) return null;
        var started = DateTimeOffset.Now;
        try
        {
            return await Task.Run(async () =>
            {
                using var scope = GitRunner.BeginOperation($"Download {ImageDiff.FormatSize(pointer.Size)} from Git LFS", background);
                try
                {
                    return await session.Repository.DownloadLfsObjectAsync(pointer, maxBytes);
                }
                catch (Exception e)
                {
                    scope.Fail(e);
                    throw;
                }
            });
        }
        catch (Exception e) when (e is GitException or IOException or UnauthorizedAccessException)
        {
            this.shell.ForgetAnswersUsedSince(started);
            this.lfsFailures[pointer.Oid] = e is GitCommandException { Result.StderrText: { Length: > 0 } stderr } ? stderr.Split('\n')[0] : e.Message;
            return null;
        }
        catch (ObjectDisposedException)
        {
            return null; // The tab was closed.
        }
    }

    /// <summary>Reads <see cref="LfsSetup"/> (on open, after setting up, and on focus while the banner shows).</summary>
    public async Task CheckLfsAsync()
    {
        if (this.Session is not { } session) return;
        try
        {
            var (setup, installed) = await Task.Run(async () =>
            {
                using var _ = GitRunner.BeginOperation("Check Git LFS", background: true);
                var setup = await session.Repository.LfsSetupAsync();
                return (setup, setup is not LfsSetup.NotInstalled && await session.Repository.LfsInstalledAsync());
            });
            if (this.disposed) return;
            this.LfsInstalled = installed;
            this.LfsSetup = setup;
        }
        catch (Exception e) when (e is GitException or IOException or UnauthorizedAccessException or ObjectDisposedException)
        {
            // Leave the last answer: a failed check says nothing about LFS.
        }
    }

    [RelayCommand]
    private async Task SetUpLfs()
    {
        if (await this.RunAsync("Set up Git LFS", (repo, ct) => repo.SetUpLfsAsync(ct)))
        {
            await this.CheckLfsAsync();
            if (this.LfsSetup == LfsSetup.Ready) this.shell.ShowInfo("Git LFS is set up: large files are checked out, and pushes upload them.");
        }
    }

    [RelayCommand]
    private Task CheckLfsAgain() => this.CheckLfsAsync();

    [RelayCommand]
    private Task GetLfs() => this.shell.OpenUrlAsync("https://git-lfs.com");

    [RelayCommand]
    private void DismissLfsBanner() => this.LfsBannerDismissed = true;

    /// <summary>
    /// Runs a mutation on the session's queue. Failures become an error
    /// banner; the session refreshes either way. Returns false on failure.
    /// </summary>
    public async Task<bool> RunAsync(string label, Func<GitRepository, CancellationToken, Task> operation, SnapshotParts invalidates = SnapshotParts.All, CancellationToken ct = default, bool blocking = false)
    {
        if (this.Session is not { } session) return false;
        if (blocking) return await this.BlockWhileAsync(label, () => this.RunAsync(label, operation, invalidates, ct));
        var started = DateTimeOffset.Now;
        try
        {
            await session.RunAsync(label, operation, invalidates, ct);
            return true;
        }
        catch (Exception e) when (e is GitException or IOException or UnauthorizedAccessException)
        {
            this.shell.ForgetAnswersUsedSince(started);
            this.shell.ShowError(e.Message);
            return false;
        }
    }

    public async Task<T?> RunAsync<T>(string label, Func<GitRepository, CancellationToken, Task<T>> operation, SnapshotParts invalidates = SnapshotParts.All, bool blocking = false)
        where T : class
    {
        if (this.Session is not { } session) return null;
        if (blocking) return await this.BlockWhileAsync(label, () => this.RunAsync(label, operation, invalidates));
        var started = DateTimeOffset.Now;
        try
        {
            return await session.RunAsync(label, operation, invalidates);
        }
        catch (Exception e) when (e is GitException or IOException or UnauthorizedAccessException)
        {
            this.shell.ForgetAnswersUsedSince(started);
            this.shell.ShowError(e.Message);
            return null;
        }
    }

    /// <summary>
    /// Refuses input to the view while <paramref name="run"/> runs and until the views show its result: the
    /// session publishes the snapshot before the mutation returns, but the history lays out the new graph after
    /// that, and on a large repository that is the slow part.
    /// </summary>
    private async Task<T> BlockWhileAsync<T>(string label, Func<Task<T>> run)
    {
        this.blockers++;
        this.BlockingOperation = label;
        if (this.blockingTimer is null)
        {
            this.blockingTimer = new DispatcherTimer { Interval = BlockingOverlayDelay };
            this.blockingTimer.Tick += (_, _) =>
            {
                this.blockingTimer!.Stop();
                this.ShowBlockingOverlay = this.IsBlocked;
            };
        }
        if (!this.ShowBlockingOverlay)
        {
            this.blockingTimer.Interval = BlockingOverlayDelay;
            this.blockingTimer.Start();
        }
        try
        {
            return await run();
        }
        finally
        {
            // The snapshot's Apply was posted before this continuation; let it run, then wait for the history it started.
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            await this.History.WhenIdleAsync();
            if (--this.blockers == 0)
            {
                this.blockingTimer.Stop();
                this.BlockingOperation = null;
                this.ShowBlockingOverlay = false;
            }
        }
    }

    /// <summary>
    /// Switches to <paramref name="target"/> with <paramref name="operation"/> (a plain switch, a
    /// new branch, a remote branch), handling local changes as the user chose (#52): brought along
    /// as git does, or left on the old branch. Changes left on the target come back.
    /// </summary>
    public async Task<bool> SwitchAsync(string label, string target, Func<GitRepository, CancellationToken, Task> operation, bool askAboutChanges = true)
    {
        var leave = false;
        var from = this.Snapshot?.Head is { BrowsingHistory: false, Branch: { } branch } ? branch : null;
        var dirty = this.Snapshot?.Status.Entries.Any(e => e.Kind != EntryKind.Ignored) == true;
        if (askAboutChanges && dirty && from is not null && from != target)
        {
            var settings = this.shell.Settings;
            var choice = settings.SwitchChanges;
            if (choice == Services.SwitchChanges.Ask)
            {
                var dialog = new SwitchChangesDialogViewModel(from, target);
                if (!await this.shell.ShowDialogAsync(dialog) || dialog.Choice is not { } answer) return false;
                choice = answer;
                if (dialog.Remember)
                {
                    settings.SwitchChanges = answer;
                    this.shell.Store.Save();
                }
            }
            leave = choice == Services.SwitchChanges.Leave;
        }

        var outcome = await this.RunAsync(label, (repo, ct) => repo.SwitchKeepingChangesAsync(leave, c => operation(repo, c), ct), blocking: true);
        if (outcome is null) return false;
        if (outcome.RestoreFailed)
        {
            this.shell.ShowError($"The changes left on {target} could not be put back. They are kept in the stash.");
        }
        else if (outcome.WaitingChanges)
        {
            this.shell.ShowInfo($"Changes you left on {target} are kept in the stash: the working tree had other changes.");
        }
        return true;
    }

    /// <summary>A read outside the snapshot (diffs, stats), labelled as background work for the command log.</summary>
    public async Task<T?> ReadAsync<T>(string label, Func<GitRepository, Task<T>> read)
        where T : class
    {
        if (this.Session is not { } session) return null;
        try
        {
            return await Task.Run(async () =>
            {
                using var _ = GitRunner.BeginOperation(label, background: true);
                return await read(session.Repository);
            });
        }
        catch (Exception e) when (e is GitException or IOException or UnauthorizedAccessException)
        {
            // The file changed or vanished under us; the next refresh catches up.
            return null;
        }
        catch (ObjectDisposedException)
        {
            return null; // The tab was closed.
        }
    }

    [RelayCommand]
    private void ShowTab(LeftPanelTab tab) => this.LeftTab = tab;

    [RelayCommand]
    private async Task CopyBranchName()
    {
        if (this.Snapshot?.Head.Branch is not { } branch) return;
        await this.shell.SetClipboardTextAsync(branch);
        this.BranchNameCopied = true;
        DispatcherTimer.RunOnce(() => this.BranchNameCopied = false, TimeSpan.FromSeconds(1.5));
    }

    public void Dispose()
    {
        if (this.disposed) return;
        this.disposed = true;
        this.History.Dispose();
        // Dispose waits for a running mutation; do it off the UI thread.
        var session = this.Session;
        if (session is not null) Task.Run(session.Dispose);
    }
}
