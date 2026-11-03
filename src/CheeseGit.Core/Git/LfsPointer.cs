using System.Text;
using CheeseGit.Core.Diff;

namespace CheeseGit.Core.Git;

/// <summary>
/// A Git LFS pointer: the small text file git stores in place of an LFS-tracked file
/// (<c>version https://git-lfs.github.com/spec/v1</c>, <c>oid sha256:…</c>, <c>size …</c>).
/// The real content lives in the LFS store, named after <see cref="Oid"/>.
/// </summary>
public sealed record LfsPointer(string Oid, long Size, byte[] Raw)
{
    /// <summary>git-lfs itself never treats a larger blob as a pointer.</summary>
    public const int MaxSize = 1024;

    private static readonly byte[] VersionV1 = "https://git-lfs.github.com/spec/v1"u8.ToArray();

    // Pointers written by pre-1.0 releases.
    private static readonly byte[] VersionHawser = "https://hawser.github.com/spec/v1"u8.ToArray();

    /// <summary>The first 10 hex digits, as git-lfs prints it.</summary>
    public string ShortOid => this.Oid[..10];

    /// <summary>Parses <paramref name="content"/> as a pointer, or returns null when it is anything else.</summary>
    public static LfsPointer? TryParse(ReadOnlySpan<byte> content)
    {
        if (content.Length == 0 || content.Length > MaxSize || content[^1] != (byte)'\n') return null;
        string? oid = null;
        long size = -1;
        var first = true;
        foreach (var range in content[..^1].Split((byte)'\n'))
        {
            var line = content[range];
            var space = line.IndexOf((byte)' ');
            if (space <= 0) return null;
            var key = line[..space];
            var value = line[(space + 1)..];
            if (first)
            {
                if (!key.SequenceEqual("version"u8) || !(value.SequenceEqual(VersionV1) || value.SequenceEqual(VersionHawser))) return null;
                first = false;
                continue;
            }
            if (!IsKey(key)) return null;
            if (key.SequenceEqual("oid"u8))
            {
                if (!value.StartsWith("sha256:"u8) || value.Length != 7 + 64 || !IsLowerHex(value[7..])) return null;
                oid = Encoding.ASCII.GetString(value[7..]);
            }
            else if (key.SequenceEqual("size"u8))
            {
                if (value.Length is 0 or > 19 || !long.TryParse(value, System.Globalization.NumberStyles.None, null, out size)) return null;
            }
        }
        return oid is not null && size >= 0 ? new LfsPointer(oid, size, content.ToArray()) : null;
    }

    /// <summary>The canonical pointer for an object (what <c>git lfs clean</c> writes).</summary>
    public static LfsPointer For(string oid, long size) =>
        new(oid, size, Encoding.ASCII.GetBytes($"version https://git-lfs.github.com/spec/v1\noid sha256:{oid}\nsize {size}\n"));

    /// <summary>
    /// Whether a text diff is between LFS pointers rather than real content: every line is a
    /// pointer's <c>key value</c> line and one of them names the spec or an object. The diff then
    /// says nothing about the file, and staging some of its lines would write a broken pointer.
    /// Works with <c>diff.context=0</c>, where the unchanged version line is missing.
    /// </summary>
    public static bool IsPointerDiff(FileDiff diff)
    {
        if (diff.IsBinary || diff.Hunks.Count == 0) return false;
        var lines = 0;
        var names = false;
        foreach (var hunk in diff.Hunks)
        {
            foreach (var line in hunk.Lines)
            {
                // A pointer has a handful of lines (version, extensions, oid, size), on two sides.
                if (++lines > 40) return false;
                var content = line.Content.AsSpan();
                var space = content.IndexOf((byte)' ');
                if (space <= 0 || !IsKey(content[..space])) return false;
                if (content.StartsWith("version https://git-lfs.github.com/spec/"u8) || content.StartsWith("oid sha256:"u8)) names = true;
            }
        }
        return names;
    }

    private static bool IsKey(ReadOnlySpan<byte> key)
    {
        foreach (var b in key)
        {
            if (b is not ((>= (byte)'a' and <= (byte)'z') or (>= (byte)'0' and <= (byte)'9') or (byte)'.' or (byte)'-')) return false;
        }
        return key.Length > 0;
    }

    private static bool IsLowerHex(ReadOnlySpan<byte> hex)
    {
        foreach (var b in hex)
        {
            if (b is not ((>= (byte)'0' and <= (byte)'9') or (>= (byte)'a' and <= (byte)'f'))) return false;
        }
        return true;
    }
}
