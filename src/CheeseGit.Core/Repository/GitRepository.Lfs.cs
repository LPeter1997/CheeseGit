using System.Collections.Concurrent;
using CheeseGit.Core.Git;

namespace CheeseGit.Core.Repository;

/// <summary>Whether Git LFS works for a repository that uses it.</summary>
public enum LfsSetup
{
    /// <summary>No attributes file sends anything to LFS.</summary>
    NotUsed,

    /// <summary>git-lfs is installed, its filter is configured and the repository's pre-push hook uploads.</summary>
    Ready,

    /// <summary>The repository uses LFS, but <c>git lfs</c> does not run.</summary>
    NotInstalled,

    /// <summary>
    /// git-lfs is installed, but its filter is not configured (checkouts leave pointer files and new
    /// files are committed whole) or the pre-push hook is missing (pushes leave the objects behind).
    /// </summary>
    NotConfigured,
}

/// <summary>Git LFS: pointers, the local object store, downloads, setup and tracking.</summary>
public sealed partial class GitRepository
{
    private readonly ConcurrentDictionary<string, Task<GitResult>> lfsDownloads = new(StringComparer.Ordinal);
    private string? lfsStorage;

    /// <summary>
    /// Cheap enough for every status refresh (two file system checks), so the attribute lookup that
    /// marks LFS files only runs where it can find any. The <c>lfs</c> folder exists once git-lfs
    /// has run in the repository (a clone, a checkout, an install); a root attributes file covers a
    /// repository that started using LFS before that.
    /// </summary>
    private bool MayUseLfs()
    {
        if (Directory.Exists(Path.Combine(this.CommonDir, "lfs"))) return true;
        try
        {
            var attributes = Path.Combine(this.Root, ".gitattributes");
            return File.Exists(attributes) && File.ReadAllText(attributes).Contains("filter=lfs", StringComparison.Ordinal);
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <summary>
    /// The paths among <paramref name="paths"/> whose <c>filter</c> attribute is <c>lfs</c>: by the
    /// attributes of <paramref name="source"/> (a commit's files: paths that moved since would not
    /// match today's patterns) from git 2.40 on, which added <c>check-attr --source</c>; by the
    /// working tree's otherwise.
    /// </summary>
    public async Task<HashSet<GitPath>> LfsPathsAsync(IReadOnlyCollection<GitPath> paths, string? source = null, CancellationToken ct = default)
    {
        var lfs = new HashSet<GitPath>();
        if (paths.Count == 0 || !this.MayUseLfs()) return lfs;
        List<string> args = ["check-attr", "-z", "--stdin"];
        if (source is not null && this.Version.CompareTo(new GitVersion(2, 40, 0, "2.40.0")) >= 0) args.AddRange(["--source", source]);
        args.Add("filter");
        var result = await this.ReadAsync("Read attributes", args, GitPath.ToNulSeparated(paths), ct);
        // "<path>\0filter\0<value>\0" per path.
        var records = new Records(result.Stdout, 0);
        while (records.Next(out var path) && records.Next(out _) && records.Next(out var value))
        {
            if (value.SequenceEqual("lfs"u8)) lfs.Add(GitPath.FromBytes(path));
        }
        return lfs;
    }

    private async Task<IReadOnlyList<FileChange>> MarkLfsAsync(IReadOnlyList<FileChange> changes, string? source, CancellationToken ct)
    {
        var lfs = await this.LfsPathsAsync(changes.Select(c => c.Path).ToList(), source, ct);
        return lfs.Count == 0 ? changes : changes.Select(c => lfs.Contains(c.Path) ? c with { IsLfs = true } : c).ToList();
    }

    /// <summary>
    /// Where git-lfs keeps objects: <c>lfs.storage</c> (relative to the git dir), else <c>lfs</c> in
    /// the common git dir, which linked worktrees share (<c>--git-path lfs</c> would name the worktree's own).
    /// </summary>
    private async Task<string> LfsStorageAsync(CancellationToken ct)
    {
        if (this.lfsStorage is { } known) return known;
        var configured = await this.TryReadAsync(["config", "--get", "lfs.storage"], ct: ct);
        var value = configured.Success ? configured.StdoutText.Trim() : "";
        return this.lfsStorage = value.Length > 0 ? Path.GetFullPath(value, this.CommonDir) : Path.Combine(this.CommonDir, "lfs");
    }

    /// <summary>The real content of an LFS file from the local store, or a missing version when it was never downloaded.</summary>
    internal async Task<FileContent> ReadLfsObjectAsync(LfsPointer pointer, long maxBytes, CancellationToken ct)
    {
        var storage = await this.LfsStorageAsync(ct);
        var file = new FileInfo(Path.Combine(storage, "objects", pointer.Oid[..2], pointer.Oid[2..4], pointer.Oid));
        // An interrupted download leaves its partial file in lfs/tmp, never here; a size check catches the rest.
        if (!file.Exists || file.Length != pointer.Size) return new FileContent(pointer.Size, null) { Lfs = pointer, IsMissing = true };
        return new FileContent(pointer.Size, pointer.Size <= maxBytes ? await File.ReadAllBytesAsync(file.FullName, ct) : null) { Lfs = pointer };
    }

    /// <summary>
    /// Downloads an LFS object with <c>git lfs smudge</c> (pointer in, content out), which also puts
    /// it into the local store. Uses the repository's LFS endpoint and credentials as git-lfs would;
    /// concurrent requests for one object share a download. Throws when it cannot be downloaded.
    /// </summary>
    public async Task<FileContent> DownloadLfsObjectAsync(LfsPointer pointer, long maxBytes, CancellationToken ct = default)
    {
        var download = this.lfsDownloads.GetOrAdd(pointer.Oid, _ => this.Git.RunAsync(this.Root, ["lfs", "smudge"], GitAccess.Read, pointer.Raw));
        GitResult result;
        try
        {
            result = await download.WaitAsync(ct);
        }
        finally
        {
            if (download.IsCompleted) this.lfsDownloads.TryRemove(new KeyValuePair<string, Task<GitResult>>(pointer.Oid, download));
        }
        result.EnsureSuccess("Download from Git LFS");
        // GIT_LFS_SKIP_SMUDGE in the environment makes smudge echo the pointer instead.
        if (result.Stdout.LongLength != pointer.Size)
        {
            throw new GitOperationException(GitErrorCode.UnexpectedOutput, $"Git LFS returned {result.Stdout.LongLength} bytes for an object of {pointer.Size} bytes.");
        }
        return new FileContent(pointer.Size, pointer.Size <= maxBytes ? result.Stdout : null) { Lfs = pointer };
    }

    /// <summary>Whether <c>git lfs</c> runs (git-lfs is installed where git finds it).</summary>
    public async Task<bool> LfsInstalledAsync(CancellationToken ct = default) => (await this.TryReadAsync(["lfs", "version"], ct: ct)).Success;

    /// <summary>Whether this repository uses LFS and, if it does, whether git-lfs is ready for it.</summary>
    public async Task<LfsSetup> LfsSetupAsync(CancellationToken ct = default)
    {
        if (!await this.UsesLfsAsync(ct)) return LfsSetup.NotUsed;
        if (!await this.LfsInstalledAsync(ct)) return LfsSetup.NotInstalled;
        if (!await this.HasLfsFilterAsync(ct)) return LfsSetup.NotConfigured;
        // Without the hook, a push sends commits whose LFS objects never reach the server.
        this.gitPaths.TryRemove("hooks/pre-push", out _);
        var hook = await this.GitPathAsync("hooks/pre-push", ct);
        try
        {
            if (!File.Exists(hook) || !File.ReadAllText(hook).Contains("lfs", StringComparison.Ordinal)) return LfsSetup.NotConfigured;
        }
        catch (IOException)
        {
            return LfsSetup.NotConfigured;
        }
        return LfsSetup.Ready;
    }

    /// <summary>Whether a tracked attributes file (or <c>info/attributes</c>) sends files to LFS.</summary>
    private async Task<bool> UsesLfsAsync(CancellationToken ct)
    {
        var files = new List<string> { await this.GitPathAsync("info/attributes", ct) };
        // The glob needs pathspec magic, which GIT_LITERAL_PATHSPECS turns off.
        var listed = await this.ReadAsync("List attributes files", ["ls-files", "-z", "--", ":(glob)**/.gitattributes"], ct: ct, literalPathspecs: false);
        var records = new Records(listed.Stdout, 0);
        while (records.Next(out var path)) files.Add(GitPath.FromBytes(path).ToFullPath(this.Root));
        foreach (var file in files)
        {
            try
            {
                if (File.Exists(file) && (await File.ReadAllTextAsync(file, ct)).Contains("filter=lfs", StringComparison.Ordinal)) return true;
            }
            catch (IOException)
            {
                // Unreadable: it decides nothing.
            }
        }
        return false;
    }

    /// <summary>
    /// Fixes what <see cref="LfsSetupAsync"/> found. Without the filter: <c>git lfs install</c> (the
    /// filter in the user's config, as git-lfs's own instructions do, and the hooks here), then
    /// <c>git lfs pull</c>, which replaces the pointer files a checkout without the filter left behind.
    /// With the filter but no hooks: <c>git lfs update</c>, which installs the hooks only (and refuses
    /// to replace a pre-push hook of the user's own).
    /// </summary>
    public async Task SetUpLfsAsync(CancellationToken ct = default)
    {
        if (await this.HasLfsFilterAsync(ct))
        {
            await this.WriteAsync("Install Git LFS hooks", ["lfs", "update"], ct: ct);
            return;
        }
        await this.WriteAsync("Set up Git LFS", ["lfs", "install"], ct: ct);
        if ((await this.ListRemotesAsync(ct)).Count > 0) await this.WriteAsync("Download Git LFS files", ["lfs", "pull"], ct: ct);
    }

    private async Task<bool> HasLfsFilterAsync(CancellationToken ct)
    {
        var process = await this.TryReadAsync(["config", "--get", "filter.lfs.process"], ct: ct);
        var smudge = await this.TryReadAsync(["config", "--get", "filter.lfs.smudge"], ct: ct);
        return process.StdoutText.Trim().Length > 0 || smudge.StdoutText.Trim().Length > 0;
    }

    /// <summary>
    /// Sends files matching <paramref name="pattern"/> (<c>*.psd</c>) to LFS from now on: <c>git lfs
    /// track</c> adds it to <c>.gitattributes</c>. Staged files among <paramref name="staged"/> are
    /// staged again (<c>--renormalize</c>: unchanged files would be skipped), so the index holds
    /// pointers, and so is <c>.gitattributes</c>, which every clone needs to treat them as LFS files.
    /// </summary>
    public async Task TrackWithLfsAsync(string pattern, IReadOnlyCollection<GitPath> staged, CancellationToken ct = default)
    {
        if (pattern.Length == 0 || pattern.StartsWith('-') || pattern.Any(char.IsControl))
        {
            throw new GitOperationException(GitErrorCode.InvalidName, $"'{pattern}' is not a pattern git-lfs can track.");
        }
        await this.WriteAsync("Track with Git LFS", ["lfs", "track", pattern], ct: ct);
        if (staged.Count == 0) return;
        await this.WriteAsync("Stage as Git LFS files", ["add", "--renormalize", .. PathspecStdin], GitPath.ToNulSeparated(staged), ct);
        await this.WriteAsync("Stage .gitattributes", ["add", .. PathspecStdin], GitPath.ToNulSeparated([GitPath.FromString(".gitattributes")]), ct);
    }
}
