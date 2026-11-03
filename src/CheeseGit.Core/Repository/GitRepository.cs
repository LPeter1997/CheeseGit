using System.Collections.Concurrent;
using System.Text;
using CheeseGit.Core.Git;

namespace CheeseGit.Core.Repository;

/// <summary>
/// One repository's git operations. Stateless apart from caches: every query
/// asks git, every mutation runs git, and outcomes are decided from exit
/// codes and repository state. Ordering and refresh are the job of
/// <see cref="RepositorySession"/>; this class is safe to call concurrently
/// for reads, but mutations must be serialized by the caller.
/// </summary>
public sealed partial class GitRepository : IDisposable
{
    private readonly ConcurrentDictionary<string, string> gitPaths = new(StringComparer.Ordinal);
    private readonly Lazy<CatFileProcess> catFile;

    private GitRepository(GitRunner git, GitVersion version, string root, string gitDir, string commonDir)
    {
        this.Git = git;
        this.Version = version;
        this.Root = root;
        this.GitDir = gitDir;
        this.CommonDir = commonDir;
        this.catFile = new Lazy<CatFileProcess>(() => new CatFileProcess(git, root), LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public GitRunner Git { get; }

    public GitVersion Version { get; }

    /// <summary>Absolute path of the working tree root.</summary>
    public string Root { get; }

    /// <summary>Absolute git dir of this worktree (not necessarily "&lt;root&gt;/.git").</summary>
    public string GitDir { get; }

    /// <summary>Absolute shared git dir (differs from <see cref="GitDir"/> in linked worktrees).</summary>
    public string CommonDir { get; }

    /// <summary>The folder name, for display.</summary>
    public string Name => Path.GetFileName(this.Root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

    internal CatFileProcess CatFile => this.catFile.Value;

    /// <summary>
    /// Opens the repository containing <paramref name="path"/> (any directory
    /// inside the working tree resolves to its root).
    /// </summary>
    public static async Task<GitRepository> OpenAsync(string path, GitRunner? git = null, CancellationToken ct = default)
    {
        git ??= new GitRunner();
        if (!Directory.Exists(path)) throw new GitOperationException(GitErrorCode.NotFound, $"'{path}' does not exist.");
        var version = await GitVersion.DetectAsync(git, ct, path);
        if (version.CompareTo(GitVersion.Minimum) < 0)
        {
            throw new GitOperationException(GitErrorCode.UnsupportedGitVersion, $"git {version} is too old; CheeseGit needs git {GitVersion.Minimum} or newer.");
        }

        var bare = await git.RunAsync(path, ["rev-parse", "--is-bare-repository"], ct: ct);
        if (!bare.Success) throw new GitOperationException(GitErrorCode.NotARepository, $"'{path}' is not a git repository.");
        if (bare.StdoutText.Trim() == "true") throw new GitOperationException(GitErrorCode.BareRepository, $"'{path}' is a bare repository, which has no working tree.");

        // --show-toplevel fails inside the git dir itself ("must be run in a work tree").
        var result = await git.RunAsync(path, ["rev-parse", "--path-format=absolute", "--show-toplevel", "--git-dir", "--git-common-dir"], ct: ct);
        var lines = result.StdoutText.Split('\n');
        if (!result.Success || lines.Length < 3) throw new GitOperationException(GitErrorCode.NotARepository, $"'{path}' is not inside a working tree.");
        return new GitRepository(git, version, Path.GetFullPath(lines[0]), Path.GetFullPath(lines[1]), Path.GetFullPath(lines[2]));
    }

    /// <summary>Creates a repository at <paramref name="path"/> (creating the directory if needed) and opens it.</summary>
    public static async Task<GitRepository> InitAsync(string path, GitRunner? git = null, CancellationToken ct = default)
    {
        git ??= new GitRunner();
        Directory.CreateDirectory(path);
        await git.RunCheckedAsync("Initialize repository", path, ["init", "-q"], GitAccess.Write, ct: ct);
        return await OpenAsync(path, git, ct);
    }

    /// <summary>
    /// Clones <paramref name="url"/> into a new folder under <paramref name="parentFolder"/>
    /// named after the repository (as git would name it), and opens it.
    /// </summary>
    public static async Task<GitRepository> CloneAsync(string url, string parentFolder, GitRunner? git = null, CancellationToken ct = default)
    {
        git ??= new GitRunner();
        var target = Path.Combine(parentFolder, DirectoryNameForUrl(url));
        if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any())
        {
            throw new GitOperationException(GitErrorCode.InvalidName, $"'{target}' already exists and is not empty.");
        }
        await git.RunCheckedAsync("Clone repository", parentFolder, ["clone", "-q", "--end-of-options", url, target], GitAccess.Write, ct: ct);
        return await OpenAsync(target, git, ct);
    }

    /// <summary>The folder name git would pick: last path segment without ".git" (and without a trailing ":" port part).</summary>
    public static string DirectoryNameForUrl(string url)
    {
        var trimmed = url.TrimEnd('/', '\\');
        if (trimmed.EndsWith("/.git", StringComparison.Ordinal)) trimmed = trimmed[..^5];
        var name = trimmed[(trimmed.LastIndexOfAny(['/', '\\', ':']) + 1)..];
        if (name.EndsWith(".git", StringComparison.Ordinal)) name = name[..^4];
        if (name.EndsWith(".bundle", StringComparison.Ordinal)) name = name[..^7];
        return name.Length == 0 ? "repository" : name;
    }

    /// <summary>
    /// Absolute path of a file in the git dir (<c>MERGE_HEAD</c>, <c>index</c>,
    /// <c>refs/...</c>), resolved by git so worktrees and submodules work.
    /// </summary>
    public async Task<string> GitPathAsync(string name, CancellationToken ct = default)
    {
        if (this.gitPaths.TryGetValue(name, out var cached)) return cached;
        var result = await this.ReadAsync("Resolve git path", ["rev-parse", "--path-format=absolute", "--git-path", name], ct: ct);
        var resolved = Path.GetFullPath(result.StdoutText.TrimEnd('\n'));
        this.gitPaths[name] = resolved;
        return resolved;
    }

    internal Task<GitResult> ReadAsync(string operation, IReadOnlyList<string> args, ReadOnlyMemory<byte> stdin = default, CancellationToken ct = default, bool literalPathspecs = true) =>
        this.Git.RunCheckedAsync(operation, this.Root, args, GitAccess.Read, stdin, ct, literalPathspecs);

    internal Task<GitResult> WriteAsync(string operation, IReadOnlyList<string> args, ReadOnlyMemory<byte> stdin = default, CancellationToken ct = default, bool literalPathspecs = true, IProgress<GitProgress>? progress = null) =>
        this.Git.RunCheckedAsync(operation, this.Root, args, GitAccess.Write, stdin, ct, literalPathspecs,
            progress is null ? null : line => progress.Report(GitProgress.Parse(line)));

    internal Task<GitResult> TryReadAsync(IReadOnlyList<string> args, ReadOnlyMemory<byte> stdin = default, CancellationToken ct = default) =>
        this.Git.RunAsync(this.Root, args, GitAccess.Read, stdin, ct);

    internal Task<GitResult> TryWriteAsync(IReadOnlyList<string> args, ReadOnlyMemory<byte> stdin = default, CancellationToken ct = default) =>
        this.Git.RunAsync(this.Root, args, GitAccess.Write, stdin, ct);

    /// <summary>Resolves a revision to a commit id, or null if it does not exist.</summary>
    public async Task<Oid?> ResolveAsync(string revision, CancellationToken ct = default)
    {
        var result = await this.TryReadAsync(["rev-parse", "--verify", "--quiet", "--end-of-options", revision + "^{commit}"], ct: ct);
        return result.Success && Oid.TryParseHex(result.StdoutText.Trim(), out var oid) ? oid : null;
    }

    internal async Task<bool> RefExistsAsync(string fullName, CancellationToken ct) =>
        (await this.TryReadAsync(["show-ref", "--verify", "--quiet", fullName], ct: ct)).Success;

    /// <summary>Throws <see cref="GitOperationException"/> unless <paramref name="name"/> is a valid branch name.</summary>
    internal async Task ValidateBranchNameAsync(string name, CancellationToken ct)
    {
        if (name.StartsWith('-') || !(await this.TryReadAsync(["check-ref-format", "--branch", name], ct: ct)).Success)
        {
            throw new GitOperationException(GitErrorCode.InvalidName, $"'{name}' is not a valid branch name.");
        }
    }

    internal static void ValidateRemoteName(string name)
    {
        if (name.Length == 0 || name.StartsWith('-') || name.Any(char.IsControl))
        {
            throw new GitOperationException(GitErrorCode.InvalidName, $"'{name}' is not a valid remote name.");
        }
    }

    public void Dispose()
    {
        if (this.catFile.IsValueCreated) this.catFile.Value.Dispose();
    }

    /// <summary>Splits git output on a separator byte without allocating per record.</summary>
    internal ref struct Records(ReadOnlySpan<byte> text, byte separator)
    {
        private ReadOnlySpan<byte> rest = text;

        public bool Next(out ReadOnlySpan<byte> record)
        {
            if (this.rest.IsEmpty)
            {
                record = default;
                return false;
            }
            var at = this.rest.IndexOf(separator);
            record = at < 0 ? this.rest : this.rest[..at];
            this.rest = at < 0 ? [] : this.rest[(at + 1)..];
            return true;
        }
    }

    internal static string Utf8(ReadOnlySpan<byte> bytes) => Encoding.UTF8.GetString(bytes);
}
