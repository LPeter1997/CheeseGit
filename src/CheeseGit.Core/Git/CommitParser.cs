using System.Text;

namespace CheeseGit.Core.Git;

public sealed record Signature(string Name, string Email, DateTimeOffset When);

/// <summary>Metadata of one commit, decoded for display.</summary>
public sealed record CommitInfo(Oid Oid, IReadOnlyList<Oid> Parents, Signature Author, Signature Committer, string Message)
{
    /// <summary>The first paragraph of the message joined into one line, as git's <c>%s</c>.</summary>
    public string Summary
    {
        get
        {
            var text = this.Message.TrimStart('\n');
            var end = text.IndexOf("\n\n", StringComparison.Ordinal);
            return (end < 0 ? text : text[..end]).Trim().ReplaceLineEndings(" ");
        }
    }

    /// <summary>Everything after the summary paragraph, without surrounding blank lines.</summary>
    public string Body
    {
        get
        {
            var text = this.Message.TrimStart('\n');
            var end = text.IndexOf("\n\n", StringComparison.Ordinal);
            return end < 0 ? "" : text[(end + 2)..].Trim('\n');
        }
    }
}

/// <summary>Parses raw commit objects (as returned by <c>cat-file</c>).</summary>
public static class CommitParser
{
    public static CommitInfo Parse(Oid oid, ReadOnlySpan<byte> raw)
    {
        Signature? author = null, committer = null;
        var parents = new List<Oid>(2);
        var encoding = Encoding.UTF8;

        // Headers until the first empty line; continuation lines start with a space.
        while (!raw.IsEmpty)
        {
            var nl = raw.IndexOf((byte)'\n');
            var line = nl < 0 ? raw : raw[..nl];
            raw = nl < 0 ? [] : raw[(nl + 1)..];
            if (line.IsEmpty) break;
            if (line.StartsWith("parent "u8) && Oid.TryParseHex(line["parent "u8.Length..], out var parent)) parents.Add(parent);
            else if (line.StartsWith("author "u8)) author = ParseSignature(line["author "u8.Length..]);
            else if (line.StartsWith("committer "u8)) committer = ParseSignature(line["committer "u8.Length..]);
            else if (line.StartsWith("encoding "u8)) encoding = TryGetEncoding(line["encoding "u8.Length..]) ?? encoding;
        }

        var unknown = new Signature("", "", DateTimeOffset.UnixEpoch);
        return new CommitInfo(oid, parents, author ?? unknown, committer ?? author ?? unknown, encoding.GetString(raw));
    }

    // "Name <email> 1700000000 +0100"
    internal static Signature ParseSignature(ReadOnlySpan<byte> s)
    {
        var lt = s.IndexOf((byte)'<');
        var gt = s.LastIndexOf((byte)'>');
        if (lt < 0 || gt < lt) return new Signature(Encoding.UTF8.GetString(s), "", DateTimeOffset.UnixEpoch);
        var name = Encoding.UTF8.GetString(s[..lt]).Trim();
        var email = Encoding.UTF8.GetString(s[(lt + 1)..gt]);
        var rest = Encoding.ASCII.GetString(s[(gt + 1)..]).Trim().Split(' ');
        var when = DateTimeOffset.UnixEpoch;
        if (rest.Length >= 1 && long.TryParse(rest[0], out var seconds))
        {
            var offset = TimeSpan.Zero;
            if (rest.Length >= 2 && rest[1].Length == 5 && int.TryParse(rest[1].AsSpan(1, 2), out var h) && int.TryParse(rest[1].AsSpan(3, 2), out var m))
            {
                offset = new TimeSpan(h, m, 0) * (rest[1][0] == '-' ? -1 : 1);
            }
            try
            {
                when = DateTimeOffset.FromUnixTimeSeconds(seconds).ToOffset(offset);
            }
            catch (ArgumentOutOfRangeException)
            {
                // Corrupt or absurd dates exist in real histories; show them as the epoch.
            }
        }
        return new Signature(name, email, when);
    }

    private static Encoding? TryGetEncoding(ReadOnlySpan<byte> name)
    {
        try
        {
            return Encoding.GetEncoding(Encoding.ASCII.GetString(name));
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
