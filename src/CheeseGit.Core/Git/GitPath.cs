using System.Buffers;
using System.Text;
using System.Text.Unicode;

namespace CheeseGit.Core.Git;

/// <summary>
/// A repository-relative path exactly as git stores it: a byte string with
/// '/' separators. Paths are opaque bytes (they need not be valid UTF-8); the
/// string form is for display and for addressing files on disk.
/// </summary>
public readonly struct GitPath : IEquatable<GitPath>, IComparable<GitPath>
{
    private readonly byte[]? bytes;

    private GitPath(byte[] bytes) => this.bytes = bytes;

    public static GitPath FromBytes(ReadOnlySpan<byte> bytes) => new(bytes.ToArray());

    /// <summary>
    /// Encodes as UTF-8. On Windows '\' is a separator and becomes '/'; on
    /// other systems it is a legal file name character and is kept.
    /// </summary>
    public static GitPath FromString(string path) =>
        new(Encoding.UTF8.GetBytes(Path.DirectorySeparatorChar == '\\' ? path.Replace('\\', '/') : path));

    public static implicit operator GitPath(string path) => FromString(path);

    public ReadOnlySpan<byte> Bytes => this.bytes;

    public bool IsEmpty => this.bytes is null || this.bytes.Length == 0;

    /// <summary>The file or directory name (after the last '/').</summary>
    public string Name
    {
        get
        {
            var s = this.ToString();
            return s[(s.LastIndexOf('/') + 1)..];
        }
    }

    /// <summary>Absolute path of this file under <paramref name="root"/> on the local file system.</summary>
    public string ToFullPath(string root) => Path.Combine(root, this.ToString().Replace('/', Path.DirectorySeparatorChar));

    /// <summary>Display form. Invalid UTF-8 shows as U+FFFD.</summary>
    public override string ToString() => this.bytes is null ? "" : Encoding.UTF8.GetString(this.bytes);

    public bool Equals(GitPath other) => this.Bytes.SequenceEqual(other.Bytes);

    public override bool Equals(object? obj) => obj is GitPath other && this.Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.AddBytes(this.Bytes);
        return hash.ToHashCode();
    }

    /// <summary>Byte order, which is git's index order.</summary>
    public int CompareTo(GitPath other) => this.Bytes.SequenceCompareTo(other.Bytes);

    public static bool operator ==(GitPath left, GitPath right) => left.Equals(right);

    public static bool operator !=(GitPath left, GitPath right) => !left.Equals(right);

    /// <summary>
    /// The path as a command-line pathspec. Arguments reach git as UTF-8, so a
    /// path that is not valid UTF-8 cannot be passed literally; it becomes a
    /// <c>:(glob)</c> pattern with one '?' per invalid byte (and glob
    /// characters escaped), which needs <c>literalPathspecs: false</c> and
    /// can match other paths, so callers filter the output by exact bytes.
    /// Prefer stdin (<c>--pathspec-from-file</c>) where the command has it.
    /// </summary>
    public (string Spec, bool Literal) ToPathspec()
    {
        var rest = this.Bytes;
        if (Utf8.IsValid(rest)) return (this.ToString(), true);
        var sb = new StringBuilder(":(glob)");
        while (!rest.IsEmpty)
        {
            if (Rune.DecodeFromUtf8(rest, out var rune, out var consumed) != OperationStatus.Done)
            {
                sb.Append('?', consumed);
            }
            else
            {
                if (rune.Value is '*' or '?' or '[' or '\\') sb.Append('\\');
                sb.Append(rune.ToString());
            }
            rest = rest[consumed..];
        }
        return (sb.ToString(), false);
    }

    /// <summary>
    /// Encodes paths as NUL-terminated records for
    /// <c>--pathspec-from-file=- --pathspec-file-nul</c> or <c>--stdin -z</c>.
    /// Passing paths on stdin avoids command-line length limits (old app, #47).
    /// </summary>
    public static byte[] ToNulSeparated(IEnumerable<GitPath> paths)
    {
        using var ms = new MemoryStream();
        foreach (var p in paths)
        {
            ms.Write(p.Bytes);
            ms.WriteByte(0);
        }
        return ms.ToArray();
    }
}
