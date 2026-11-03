using CheeseGit.Core.Git;

namespace CheeseGit.Core.Repository;

public sealed record SessionOptions
{
    /// <summary>Watch the file system for changes made outside CheeseGit.</summary>
    public bool Watch { get; init; } = true;

    /// <summary>How long file system events are collected before one refresh runs.</summary>
    public TimeSpan Debounce { get; init; } = TimeSpan.FromMilliseconds(300);

    /// <summary>Full refresh interval, used only when watching is off or unavailable.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(30);
}

/// <summary>
/// The single owner of one open repository's state (docs/architecture.md, State and refresh).
/// <list type="bullet">
/// <item>Holds an immutable <see cref="RepoSnapshot"/> and publishes a new one
/// only when its content changed.</item>
/// <item>Runs mutations one at a time. Snapshot refreshes take the same queue,
/// so a refresh that started before a mutation can never publish its stale
/// result after it. The old app ran mutations concurrently and patched the
/// UI with optimistic updates, sequencers and rollbacks.</item>
/// <item>Refreshes in response to its own mutations and to targeted file
/// system invalidations; it polls only when watching is unavailable.</item>
/// </list>
/// Reads that are not part of the snapshot (diffs, history, file contents)
/// go straight to <see cref="Repository"/> and run concurrently.
/// </summary>
public sealed class RepositorySession : IDisposable
{
    private readonly SemaphoreSlim queue = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private readonly RepositoryWatcher? watcher;
    private readonly Timer? poll;
    private int pendingParts;
    private CancellationTokenSource? backgroundRefresh;
    private int waitingMutations;
    private RepoSnapshot snapshot;
    private string? currentOperation;

    private RepositorySession(GitRepository repository, RepoSnapshot snapshot, SessionOptions options)
    {
        this.Repository = repository;
        this.snapshot = snapshot;
        if (options.Watch)
        {
            this.watcher = new RepositoryWatcher(repository, options.Debounce);
            this.watcher.Invalidated += this.Invalidate;
            this.IsWatching = this.watcher.Start();
        }
        if (!this.IsWatching)
        {
            this.poll = new Timer(_ => this.Invalidate(SnapshotParts.All), null, options.PollInterval, options.PollInterval);
        }
    }

    public GitRepository Repository { get; }

    /// <summary>The latest published state. Never null; replaced, never mutated.</summary>
    public RepoSnapshot Snapshot => Volatile.Read(ref this.snapshot);

    /// <summary>False when the file system cannot be watched and the session polls instead.</summary>
    public bool IsWatching { get; }

    /// <summary>Label of the mutation that is running, or null when idle.</summary>
    public string? CurrentOperation => Volatile.Read(ref this.currentOperation);

    /// <summary>
    /// Raised on a background thread after a new snapshot is published, only
    /// when something changed, in publication order. Subscribers marshal to
    /// the UI thread themselves and must not block on the session.
    /// </summary>
    public event Action<SnapshotChange>? SnapshotChanged;

    /// <summary>Raised on a background thread when <see cref="CurrentOperation"/> changes.</summary>
    public event Action<string?>? CurrentOperationChanged;

    /// <summary>
    /// Raised on a background thread after every refresh with the parts that
    /// were re-read, whether or not they changed. Views of data outside the
    /// snapshot use it: a file edited twice stays "modified", so the snapshot
    /// does not change, but its diff does.
    /// </summary>
    public event Action<SnapshotParts>? Refreshed;

    /// <summary>A background refresh failed (for example, the repository was deleted).</summary>
    public event Action<Exception>? RefreshFailed;

    internal RepositoryWatcher? Watcher => this.watcher;

    public static async Task<RepositorySession> OpenAsync(string path, SessionOptions? options = null, GitRunner? git = null, CancellationToken ct = default)
    {
        var repository = await GitRepository.OpenAsync(path, git, ct);
        try
        {
            var snapshot = await ReadAsync(repository, SnapshotParts.All, null, ct);
            return new RepositorySession(repository, snapshot, options ?? new SessionOptions());
        }
        catch
        {
            repository.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Runs a mutation after every earlier one has finished, then refreshes
    /// <paramref name="invalidates"/> (plus anything the watcher reported
    /// meanwhile) before returning, also when the mutation throws, since it
    /// may have changed the repository before failing.
    /// </summary>
    public async Task<T> RunAsync<T>(
        string label,
        Func<GitRepository, CancellationToken, Task<T>> operation,
        SnapshotParts invalidates = SnapshotParts.All,
        CancellationToken ct = default)
    {
        // The mutation refreshes afterwards anyway, so a background refresh
        // in its way is stopped rather than waited for: a fetch once queued
        // for 112 s behind a git status on Windows. Reads take no locks, so
        // killing them is safe; DrainAsync hands their parts back to us.
        // Interlocked on both sides (here and in DrainAsync) so that one of
        // them sees the other: either the refresh is cancelled here, or it
        // sees the waiting mutation and stops itself.
        Interlocked.Increment(ref this.waitingMutations);
        try
        {
            CancelQuietly(Volatile.Read(ref this.backgroundRefresh));
            await this.queue.WaitAsync(ct);
        }
        finally
        {
            Interlocked.Decrement(ref this.waitingMutations);
        }
        try
        {
            this.SetCurrentOperation(label);
            try
            {
                using var scope = GitRunner.BeginOperation(label);
                try
                {
                    return await operation(this.Repository, ct);
                }
                catch (Exception e)
                {
                    scope.Fail(e);
                    throw;
                }
            }
            finally
            {
                this.SetCurrentOperation(null);
                await this.RefreshCoreAsync(invalidates | this.TakePending(), throwOnError: false);
            }
        }
        finally
        {
            this.queue.Release();
        }
    }

    public Task RunAsync(
        string label,
        Func<GitRepository, CancellationToken, Task> operation,
        SnapshotParts invalidates = SnapshotParts.All,
        CancellationToken ct = default) =>
        this.RunAsync<bool>(label, async (repo, token) =>
        {
            await operation(repo, token);
            return true;
        }, invalidates, ct);

    /// <summary>Re-reads <paramref name="parts"/> now (e.g. when the window gets focus) and waits for the result.</summary>
    public async Task RefreshAsync(SnapshotParts parts = SnapshotParts.All, CancellationToken ct = default)
    {
        await this.queue.WaitAsync(ct);
        try
        {
            await this.RefreshCoreAsync(parts | this.TakePending(), throwOnError: true, ct);
        }
        finally
        {
            this.queue.Release();
        }
    }

    /// <summary>
    /// Marks parts stale and refreshes them in the background. Calls made
    /// while the queue is busy coalesce into one refresh.
    /// </summary>
    public void Invalidate(SnapshotParts parts)
    {
        if (parts == SnapshotParts.None || this.lifetime.IsCancellationRequested) return;
        Interlocked.Or(ref this.pendingParts, (int)parts);
        _ = Task.Run(this.DrainAsync);
    }

    private async Task DrainAsync()
    {
        try
        {
            await this.queue.WaitAsync(this.lifetime.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        try
        {
            // An earlier drain or a mutation may already have taken the parts.
            var parts = this.TakePending();
            if (parts != SnapshotParts.None)
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(this.lifetime.Token);
                Interlocked.Exchange(ref this.backgroundRefresh, cts);
                if (Volatile.Read(ref this.waitingMutations) > 0) cts.Cancel();
                try
                {
                    await this.RefreshCoreAsync(parts, throwOnError: false, cts.Token);
                }
                finally
                {
                    Volatile.Write(ref this.backgroundRefresh, null);
                }
                // Stopped for a mutation: it re-reads these after it ran. (If the
                // refresh finished first, that is one redundant read.)
                if (cts.IsCancellationRequested && !this.lifetime.IsCancellationRequested) Interlocked.Or(ref this.pendingParts, (int)parts);
            }
        }
        finally
        {
            this.queue.Release();
        }
    }

    private static void CancelQuietly(CancellationTokenSource? cts)
    {
        try
        {
            cts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // That refresh has just finished.
        }
    }

    private SnapshotParts TakePending() => (SnapshotParts)Interlocked.Exchange(ref this.pendingParts, 0);

    /// <summary>Must be called while holding <see cref="queue"/>.</summary>
    private async Task RefreshCoreAsync(SnapshotParts parts, bool throwOnError, CancellationToken ct = default)
    {
        if (parts == SnapshotParts.None) return;
        RepoSnapshot next;
        try
        {
            next = await ReadAsync(this.Repository, parts, this.Snapshot, ct);
        }
        catch (Exception e) when (!throwOnError && e is GitException or IOException or OperationCanceledException)
        {
            if (e is not OperationCanceledException) this.RefreshFailed?.Invoke(e);
            return;
        }

        var previous = this.Snapshot;
        var changed = next.DifferencesFrom(previous);
        if (changed != SnapshotParts.None)
        {
            next = next with { Version = previous.Version + 1 };
            Volatile.Write(ref this.snapshot, next);
            this.SnapshotChanged?.Invoke(new SnapshotChange(next, changed));
        }
        this.Refreshed?.Invoke(parts);
    }

    /// <summary>Reads the requested parts concurrently; the rest is taken from <paramref name="previous"/>.</summary>
    private static async Task<RepoSnapshot> ReadAsync(GitRepository repo, SnapshotParts parts, RepoSnapshot? previous, CancellationToken ct)
    {
        Task<T>? Read<T>(SnapshotParts part, Func<CancellationToken, Task<T>> read) =>
            previous is null || (parts & part) != 0 ? read(ct) : null;

        using var scope = GitRunner.BeginOperation("Refresh", background: true);
        try
        {
            return await ReadPartsAsync();
        }
        catch (Exception e)
        {
            scope.Fail(e);
            throw;
        }

        async Task<RepoSnapshot> ReadPartsAsync()
        {
            var status = Read(SnapshotParts.Status, repo.StatusAsync);
            var head = Read(SnapshotParts.Head, repo.HeadAsync);
            var refs = Read(SnapshotParts.Refs, repo.ListRefsAsync);
            var operation = Read(SnapshotParts.Operation, repo.OperationStateAsync);
            var stashes = Read(SnapshotParts.Stashes, repo.ListStashesAsync);
            var remotes = Read(SnapshotParts.Remotes, repo.ListRemotesAsync);
            await Task.WhenAll(new Task?[] { status, head, refs, operation, stashes, remotes }.OfType<Task>());

            return new RepoSnapshot(
                head is null ? previous!.Head : await head,
                status is null ? previous!.Status : await status,
                refs is null ? previous!.Refs : await refs,
                operation is null ? previous!.Operation : await operation,
                stashes is null ? previous!.Stashes : await stashes,
                remotes is null ? previous!.Remotes : await remotes)
            {
                Version = previous?.Version ?? 0,
            };
        }
    }

    private void SetCurrentOperation(string? label)
    {
        Volatile.Write(ref this.currentOperation, label);
        this.CurrentOperationChanged?.Invoke(label);
    }

    public void Dispose()
    {
        if (this.lifetime.IsCancellationRequested) return;
        this.lifetime.Cancel();
        this.watcher?.Dispose();
        this.poll?.Dispose();
        // Let a running mutation or refresh finish before the cat-file process
        // goes away. Bounded: events are raised while the queue is held (that
        // keeps them in order), so a handler that disposes would wait forever.
        this.queue.Wait(TimeSpan.FromSeconds(10));
        this.Repository.Dispose();
        this.lifetime.Dispose();
    }
}
