using System.Text;

namespace CheeseGit.Core.Git;

public enum EntryKind : byte
{
    Changed,
    Renamed,
    Unmerged,
    Untracked,
    Ignored,
}

/// <summary>The kind of change shown for a file in a staged/unstaged list.</summary>
public enum FileStatus : byte
{
    Added,
    Modified,
    Deleted,
    Renamed,
    Copied,
    TypeChanged,
    Untracked,
    Conflicted,
}

/// <summary>A file in a staged or unstaged list.</summary>
public sealed record FileChange(GitPath Path, FileStatus Status, GitPath? OriginalPath = null)
{
    /// <summary>Tracked by Git LFS (its <c>filter</c> attribute is <c>lfs</c>), going by the working tree's attributes.</summary>
    public bool IsLfs { get; init; }
}

/// <summary>
/// One file in <c>git status</c>, as reported by porcelain v2. Paths are the
/// exact bytes git reported.
/// </summary>
public sealed record StatusEntry(EntryKind Kind, char IndexState, char WorktreeState, GitPath Path, GitPath? OriginalPath, bool IsSubmodule)
{
    /// <summary>Tracked by Git LFS (see <see cref="FileChange.IsLfs"/>).</summary>
    public bool IsLfs { get; init; }

    /// <summary>The change in the index relative to HEAD, or null.</summary>
    public FileChange? Staged => this.Kind switch
    {
        EntryKind.Changed or EntryKind.Renamed when this.IndexState != '.' => new FileChange(this.Path, ToStatus(this.IndexState), this.OriginalPath) { IsLfs = this.IsLfs },
        _ => null,
    };

    /// <summary>The change in the working tree relative to the index, or null.</summary>
    public FileChange? Unstaged => this.Kind switch
    {
        EntryKind.Untracked => new FileChange(this.Path, FileStatus.Untracked) { IsLfs = this.IsLfs },
        EntryKind.Unmerged => new FileChange(this.Path, FileStatus.Conflicted) { IsLfs = this.IsLfs },
        EntryKind.Changed or EntryKind.Renamed when this.WorktreeState != '.' => new FileChange(this.Path, ToStatus(this.WorktreeState)) { IsLfs = this.IsLfs },
        _ => null,
    };

    private static FileStatus ToStatus(char c) => c switch
    {
        'A' => FileStatus.Added,
        'D' => FileStatus.Deleted,
        'R' => FileStatus.Renamed,
        'C' => FileStatus.Copied,
        'T' => FileStatus.TypeChanged,
        _ => FileStatus.Modified,
    };
}

/// <summary>Branch header of <c>git status --branch</c>.</summary>
public sealed record BranchStatus(Oid? Head, string? Branch, string? Upstream, int? Ahead, int? Behind)
{
    public bool IsDetached => this.Branch is null;

    /// <summary>No commit yet on the current branch.</summary>
    public bool IsUnborn => this.Head is null && this.Branch is not null;
}

public sealed record RepoStatus(BranchStatus Branch, int StashCount, IReadOnlyList<StatusEntry> Entries)
{
    public IEnumerable<FileChange> Staged => this.Entries.Select(e => e.Staged).OfType<FileChange>();

    public IEnumerable<FileChange> Unstaged => this.Entries.Select(e => e.Unstaged).OfType<FileChange>();

    public bool HasConflicts => this.Entries.Any(e => e.Kind == EntryKind.Unmerged);
}

/// <summary>Parses <c>git status --porcelain=v2 -z --branch --show-stash</c>.</summary>
public static class StatusParser
{
    public static readonly string[] Args =
    [
        "status", "--porcelain=v2", "-z", "--branch", "--show-stash",
        "--untracked-files=all", "--ignore-submodules=none", "--ahead-behind",
    ];

    public static RepoStatus Parse(ReadOnlySpan<byte> output)
    {
        Oid? oid = null;
        string? head = null, upstream = null;
        int? ahead = null, behind = null;
        var stash = 0;
        var entries = new List<StatusEntry>();

        while (!output.IsEmpty)
        {
            var record = Next(ref output);
            if (record.IsEmpty) continue;
            switch ((char)record[0])
            {
                case '#':
                    ParseHeader(record, ref oid, ref head, ref upstream, ref ahead, ref behind, ref stash);
                    break;
                case '1':
                    entries.Add(ParseChanged(record, fieldsBeforePath: 8, EntryKind.Changed, originalPath: null));
                    break;
                case '2':
                    // The original path follows as its own NUL-terminated record.
                    var original = GitPath.FromBytes(Next(ref output));
                    entries.Add(ParseChanged(record, fieldsBeforePath: 9, EntryKind.Renamed, original));
                    break;
                case 'u':
                    entries.Add(ParseChanged(record, fieldsBeforePath: 10, EntryKind.Unmerged, originalPath: null));
                    break;
                case '?':
                    // A nested repository shows up as a directory ("sub/"); the
                    // staging lists operate on files only.
                    if (record[^1] == (byte)'/') break;
                    entries.Add(new StatusEntry(EntryKind.Untracked, '?', '?', GitPath.FromBytes(record[2..]), null, false));
                    break;
                case '!':
                    entries.Add(new StatusEntry(EntryKind.Ignored, '!', '!', GitPath.FromBytes(record[2..]), null, false));
                    break;
                default:
                    throw new GitOperationException(GitErrorCode.UnexpectedOutput, $"Unknown status record '{(char)record[0]}'.");
            }
        }
        return new RepoStatus(new BranchStatus(oid, head, upstream, ahead, behind), stash, entries);
    }

    private static ReadOnlySpan<byte> Next(ref ReadOnlySpan<byte> output)
    {
        var nul = output.IndexOf((byte)0);
        var record = nul < 0 ? output : output[..nul];
        output = nul < 0 ? [] : output[(nul + 1)..];
        return record;
    }

    // "<kind> <XY> <sub> ... <path>": the path is everything after the Nth space,
    // so paths containing spaces survive intact.
    private static StatusEntry ParseChanged(ReadOnlySpan<byte> record, int fieldsBeforePath, EntryKind kind, GitPath? originalPath)
    {
        var rest = record;
        ReadOnlySpan<byte> xy = default, sub = default;
        for (var field = 0; field < fieldsBeforePath; field++)
        {
            var space = rest.IndexOf((byte)' ');
            if (space < 0) throw new GitOperationException(GitErrorCode.UnexpectedOutput, "Truncated status record.");
            if (field == 1) xy = rest[..space];
            if (field == 2) sub = rest[..space];
            rest = rest[(space + 1)..];
        }
        if (xy.Length != 2) throw new GitOperationException(GitErrorCode.UnexpectedOutput, "Bad XY field in status record.");
        return new StatusEntry(kind, (char)xy[0], (char)xy[1], GitPath.FromBytes(rest), originalPath, sub.Length > 0 && sub[0] == (byte)'S');
    }

    private static void ParseHeader(
        ReadOnlySpan<byte> record,
        ref Oid? oid, ref string? head, ref string? upstream,
        ref int? ahead, ref int? behind, ref int stash)
    {
        var text = Encoding.UTF8.GetString(record);
        if (text.StartsWith("# branch.oid ", StringComparison.Ordinal))
        {
            oid = Oid.TryParseHex(text["# branch.oid ".Length..], out var parsed) ? parsed : null;
        }
        else if (text.StartsWith("# branch.head ", StringComparison.Ordinal))
        {
            var v = text["# branch.head ".Length..];
            head = v == "(detached)" ? null : v;
        }
        else if (text.StartsWith("# branch.upstream ", StringComparison.Ordinal))
        {
            upstream = text["# branch.upstream ".Length..];
        }
        else if (text.StartsWith("# branch.ab ", StringComparison.Ordinal))
        {
            var parts = text["# branch.ab ".Length..].Split(' ');
            ahead = int.Parse(parts[0].AsSpan(1));
            behind = int.Parse(parts[1].AsSpan(1));
        }
        else if (text.StartsWith("# stash ", StringComparison.Ordinal))
        {
            stash = int.Parse(text.AsSpan("# stash ".Length));
        }
    }
}
