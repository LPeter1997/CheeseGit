using CheeseGit.Core.Git;

namespace CheeseGit.Core.Repository;

/// <summary>
/// Turns file system events into targeted snapshot invalidations (docs/architecture.md, State and refresh).
/// The old app watched the whole tree, .git and node_modules included, and
/// refreshed everything on each event; running git touched .git/index, which
/// fired the watcher again: a self-sustaining refresh loop.
/// Here, git-dir events map to the snapshot parts they affect, and worktree
/// events are batched and dropped when .gitignore excludes them.
/// </summary>
internal sealed class RepositoryWatcher : IDisposable
{
    // Worktree batches larger than this skip ignore filtering and just
    // refresh status: one status call is cheaper than classifying them.
    private const int MaxFilteredBatch = 4096;

    private readonly GitRepository repo;
    private readonly TimeSpan debounce;
    private readonly List<FileSystemWatcher> watchers = [];
    private readonly IgnoreCache ignores;
    private readonly Lock gate = new();
    // Flushes can overlap when classification is slow; one at a time keeps
    // the ignore cache single-threaded.
    private readonly SemaphoreSlim flushing = new(1, 1);
    private readonly HashSet<string> pendingPaths = new(StringComparer.Ordinal);
    private SnapshotParts pendingParts;
    private bool pendingOverflow;
    private Timer? timer;
    private bool disposed;

    public RepositoryWatcher(GitRepository repo, TimeSpan debounce)
    {
        this.repo = repo;
        this.debounce = debounce;
        this.ignores = new IgnoreCache(repo);
    }

    /// <summary>Raised on a thread-pool thread after each debounced batch.</summary>
    public event Action<SnapshotParts>? Invalidated;

    /// <summary>For tests: worktree events in a batch and how many survived ignore filtering.</summary>
    internal event Action<int, int>? WorktreeBatchProcessed;

    /// <summary>
    /// Starts watching. Returns false when the platform refuses (inotify
    /// limit, network file system), so the caller can fall back to polling.
    /// </summary>
    public bool Start()
    {
        try
        {
            this.Watch(this.repo.GitDir, recursive: false, this.OnGitDirEvent);
            if (!SamePath(this.repo.CommonDir, this.repo.GitDir)) this.Watch(this.repo.CommonDir, recursive: false, this.OnGitDirEvent);
            this.Watch(Path.Combine(this.repo.CommonDir, "refs"), recursive: true, this.OnRefsEvent);
            // Dropping a stash that is not the newest only rewrites its reflog.
            var stashLogs = Path.Combine(this.repo.CommonDir, "logs", "refs");
            if (Directory.Exists(stashLogs)) this.Watch(stashLogs, recursive: false, this.OnStashLogEvent);
            this.Watch(this.repo.Root, recursive: true, this.OnWorktreeEvent);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or PlatformNotSupportedException)
        {
            this.DisposeWatchers();
            return false;
        }
    }

    private void Watch(string directory, bool recursive, FileSystemEventHandler handler)
    {
        var watcher = new FileSystemWatcher(directory)
        {
            IncludeSubdirectories = recursive,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
            InternalBufferSize = 64 * 1024,
        };
        watcher.Changed += handler;
        watcher.Created += handler;
        watcher.Deleted += handler;
        watcher.Renamed += (s, e) =>
        {
            handler(s, new FileSystemEventArgs(WatcherChangeTypes.Deleted, directory, e.OldName));
            handler(s, e);
        };
        // A buffer overflow loses events: refresh everything.
        watcher.Error += (_, _) => this.Overflow();
        this.watchers.Add(watcher);
        watcher.EnableRaisingEvents = true;
    }

    private void OnGitDirEvent(object? sender, FileSystemEventArgs e)
    {
        var parts = ClassifyGitDirEntry(e.Name ?? "");
        if (parts != SnapshotParts.None) this.Queue(parts, null);
    }

    /// <summary>Which snapshot parts a change to a top-level git-dir entry affects.</summary>
    internal static SnapshotParts ClassifyGitDirEntry(string name) => name switch
    {
        "HEAD" => SnapshotParts.Head | SnapshotParts.Status,
        // The index holds staged changes and the conflict stages.
        "index" => SnapshotParts.Status | SnapshotParts.Operation,
        "packed-refs" => SnapshotParts.Refs | SnapshotParts.Head | SnapshotParts.Status,
        "config" => SnapshotParts.Remotes | SnapshotParts.Refs | SnapshotParts.Status,
        "MERGE_HEAD" or "REVERT_HEAD" or "CHERRY_PICK_HEAD" or "rebase-merge" or "rebase-apply"
            => SnapshotParts.Operation | SnapshotParts.Status,
        // Lock files, ORIG_HEAD, FETCH_HEAD, logs, objects: nothing visible.
        _ => SnapshotParts.None,
    };

    private void OnRefsEvent(object? sender, FileSystemEventArgs e)
    {
        var name = (e.Name ?? "").Replace('\\', '/');
        if (name.EndsWith(".lock", StringComparison.Ordinal)) return;
        var parts = SnapshotParts.Refs | SnapshotParts.Head | SnapshotParts.Status;
        if (name == "stash") parts |= SnapshotParts.Stashes;
        this.Queue(parts, null);
    }

    private void OnStashLogEvent(object? sender, FileSystemEventArgs e)
    {
        if (e.Name == "stash") this.Queue(SnapshotParts.Stashes | SnapshotParts.Status, null);
    }

    private void OnWorktreeEvent(object? sender, FileSystemEventArgs e)
    {
        if (this.ToRepoRelative(e.FullPath) is { } relative) this.Queue(SnapshotParts.None, relative);
    }

    /// <summary>
    /// "dir/file" for a path in the working tree, or null for paths that
    /// cannot affect status: the git dir (watched separately) and the
    /// internals of nested repositories.
    /// </summary>
    private string? ToRepoRelative(string fullPath)
    {
        if (IsUnder(fullPath, this.repo.GitDir) || IsUnder(fullPath, this.repo.CommonDir)) return null;
        var relative = Path.GetRelativePath(this.repo.Root, fullPath);
        if (Path.DirectorySeparatorChar == '\\') relative = relative.Replace('\\', '/');
        if (relative == "." || relative.StartsWith("../", StringComparison.Ordinal)) return null;
        foreach (var segment in relative.Split('/'))
        {
            if (segment == ".git") return null;
        }
        return relative;
    }

    private void Queue(SnapshotParts parts, string? worktreePath)
    {
        lock (this.gate)
        {
            if (this.disposed) return;
            this.pendingParts |= parts;
            if (worktreePath is not null && this.pendingPaths.Count <= MaxFilteredBatch) this.pendingPaths.Add(worktreePath);
            // Fixed delay from the first event, not reset by later ones, so a
            // continuous storm still refreshes every debounce interval.
            this.timer ??= new Timer(_ => _ = this.FlushAsync(), null, this.debounce, Timeout.InfiniteTimeSpan);
        }
    }

    private void Overflow()
    {
        lock (this.gate)
        {
            this.pendingOverflow = true;
        }
        this.Queue(SnapshotParts.All, null);
    }

    private async Task FlushAsync()
    {
        await this.flushing.WaitAsync();
        try
        {
            await this.FlushCoreAsync();
        }
        finally
        {
            this.flushing.Release();
        }
    }

    private async Task FlushCoreAsync()
    {
        SnapshotParts parts;
        List<string> paths;
        bool overflow;
        lock (this.gate)
        {
            this.timer?.Dispose();
            this.timer = null;
            parts = this.pendingParts;
            paths = [.. this.pendingPaths];
            overflow = this.pendingOverflow;
            this.pendingParts = SnapshotParts.None;
            this.pendingPaths.Clear();
            this.pendingOverflow = false;
            if (this.disposed) return;
        }

        // Ignore rules depend on .gitignore files and on what is tracked.
        if (overflow || (parts & SnapshotParts.Status) != 0 || paths.Exists(p => p == ".gitignore" || p.EndsWith("/.gitignore", StringComparison.Ordinal)))
        {
            this.ignores.Clear();
        }

        if (paths.Count > 0)
        {
            var kept = paths.Count;
            try
            {
                if (paths.Count <= MaxFilteredBatch) kept = await this.ignores.CountRelevantAsync(paths);
            }
            catch (Exception e) when (e is GitException or IOException)
            {
                // Classification failed; refreshing is always safe.
            }
            this.WorktreeBatchProcessed?.Invoke(paths.Count, kept);
            if (kept > 0) parts |= SnapshotParts.Status;
        }

        if (parts != SnapshotParts.None) this.Invalidated?.Invoke(parts);
    }

    private static bool IsUnder(string path, string directory)
    {
        var comparison = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var dir = directory.TrimEnd(Path.DirectorySeparatorChar);
        return path.Equals(dir, comparison)
            || (path.StartsWith(dir, comparison) && path.Length > dir.Length && path[dir.Length] == Path.DirectorySeparatorChar);
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(Path.TrimEndingDirectorySeparator(a), Path.TrimEndingDirectorySeparator(b),
            OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);

    private void DisposeWatchers()
    {
        foreach (var w in this.watchers) w.Dispose();
        this.watchers.Clear();
    }

    public void Dispose()
    {
        lock (this.gate)
        {
            this.disposed = true;
            this.timer?.Dispose();
            this.timer = null;
        }
        this.DisposeWatchers();
    }

    /// <summary>
    /// Remembers which directories are ignored, so an event storm inside
    /// node_modules or target costs one check-ignore call, not one per event.
    /// Directory results are safe to cache: git cannot re-include a file
    /// below an excluded directory, and check-ignore does not report a
    /// directory that contains tracked files.
    /// </summary>
    private sealed class IgnoreCache(GitRepository repo)
    {
        private readonly Dictionary<string, bool> directories = new(StringComparer.Ordinal);

        public void Clear() => this.directories.Clear();

        /// <summary>Number of paths that are not ignored.</summary>
        public async Task<int> CountRelevantAsync(IReadOnlyList<string> paths)
        {
            // Ask about every unknown ancestor directory and every path whose
            // ancestors are not known to be ignored, in one call.
            var query = new HashSet<string>(StringComparer.Ordinal);
            foreach (var path in paths)
            {
                if (!this.UnderIgnoredDirectory(path, query)) query.Add(path);
            }
            var ignored = query.Count == 0 ? [] : await repo.CheckIgnoredAsync([.. query.Select(GitPath.FromString)]);
            foreach (var q in query)
            {
                if (q.EndsWith('/')) this.directories[q] = ignored.Contains(GitPath.FromString(q));
            }

            var relevant = 0;
            foreach (var path in paths)
            {
                if (!this.UnderIgnoredDirectory(path, null) && !ignored.Contains(GitPath.FromString(path))) relevant++;
            }
            return relevant;
        }

        /// <summary>
        /// True if an ancestor of <paramref name="path"/> is a known ignored
        /// directory. Collects unknown ancestors into <paramref name="unknown"/>.
        /// </summary>
        private bool UnderIgnoredDirectory(string path, HashSet<string>? unknown)
        {
            for (var slash = path.IndexOf('/'); slash >= 0; slash = path.IndexOf('/', slash + 1))
            {
                // "dir/" so that directory-only patterns ("build/") match.
                var dir = path[..(slash + 1)];
                if (this.directories.TryGetValue(dir, out var isIgnored))
                {
                    if (isIgnored) return true;
                }
                else
                {
                    unknown?.Add(dir);
                }
            }
            return false;
        }
    }
}
