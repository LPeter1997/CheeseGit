using System.Text;

namespace CheeseGit.Fixtures;

/// <summary>A file change in a generated commit. <see cref="Content"/> null deletes the path.</summary>
public sealed record FileOp(byte[] Path, byte[]? Content, string Mode = "100644")
{
    public static FileOp Write(string path, string content) => new(Encoding.UTF8.GetBytes(path), Encoding.UTF8.GetBytes(content));

    public static FileOp Write(string path, byte[] content) => new(Encoding.UTF8.GetBytes(path), content);

    public static FileOp Delete(string path) => new(Encoding.UTF8.GetBytes(path), null);
}

/// <summary>
/// Writes a <c>git fast-import</c> stream. History is built this way rather
/// than with porcelain commands because it is deterministic (explicit
/// identities, dates and parents), stores paths as raw bytes, and imports
/// 100k commits in seconds.
/// </summary>
public sealed class FastImportWriter
{
    /// <summary>Commit times start here and advance one minute per commit, so topo and date order agree.</summary>
    public const long BaseTime = 1_600_000_000;

    private readonly MemoryStream stream = new();
    private int nextMark = 1;

    public int CommitCount => this.nextMark - 1;

    /// <summary>
    /// Adds a commit on <paramref name="refName"/> and returns its mark. The
    /// first parent is explicit, so the ref's previous tip does not matter;
    /// no parents makes a root commit.
    /// </summary>
    public int Commit(string refName, string message, IEnumerable<FileOp> ops, params int[] parents)
    {
        var mark = this.nextMark++;
        var time = BaseTime + (mark * 60);
        if (parents.Length == 0) this.Line($"reset {refName}");
        this.Line($"commit {refName}");
        this.Line($"mark :{mark}");
        this.Line($"author Fixture Author <author@example.com> {time} +0000");
        this.Line($"committer Fixture Committer <committer@example.com> {time} +0000");
        this.Data(Encoding.UTF8.GetBytes(message + "\n"));
        if (parents.Length > 0) this.Line($"from :{parents[0]}");
        foreach (var p in parents.Skip(1)) this.Line($"merge :{p}");
        foreach (var op in ops)
        {
            if (op.Content is null)
            {
                this.Write("D ");
                this.QuotedPath(op.Path);
                this.Line("");
            }
            else
            {
                this.Write($"M {op.Mode} inline ");
                this.QuotedPath(op.Path);
                this.Line("");
                this.Data(op.Content);
            }
        }
        this.Line("");
        return mark;
    }

    /// <summary>Points <paramref name="refName"/> (a branch or a lightweight tag) at a commit.</summary>
    public void Ref(string refName, int mark)
    {
        this.Line($"reset {refName}");
        this.Line($"from :{mark}");
        this.Line("");
    }

    /// <summary>Adds an annotated tag.</summary>
    public void AnnotatedTag(string name, int mark, string message)
    {
        this.Line($"tag {name}");
        this.Line($"from :{mark}");
        this.Line($"tagger Fixture Tagger <tagger@example.com> {BaseTime + (mark * 60)} +0000");
        this.Data(Encoding.UTF8.GetBytes(message + "\n"));
    }

    public byte[] ToBytes()
    {
        this.Line("done");
        return this.stream.ToArray();
    }

    private void Data(byte[] content)
    {
        this.Line($"data {content.Length}");
        this.stream.Write(content);
        this.Line("");
    }

    /// <summary>C-style quoting with octal escapes, so any byte (newline, invalid UTF-8) survives.</summary>
    private void QuotedPath(byte[] path)
    {
        this.stream.WriteByte((byte)'"');
        foreach (var b in path)
        {
            switch (b)
            {
                case (byte)'"' or (byte)'\\':
                    this.stream.WriteByte((byte)'\\');
                    this.stream.WriteByte(b);
                    break;
                case < 0x20 or >= 0x7f:
                    this.Write($"\\{Convert.ToString(b, 8).PadLeft(3, '0')}");
                    break;
                default:
                    this.stream.WriteByte(b);
                    break;
            }
        }
        this.stream.WriteByte((byte)'"');
    }

    private void Line(string text)
    {
        this.Write(text);
        this.stream.WriteByte((byte)'\n');
    }

    private void Write(string text) => this.stream.Write(Encoding.UTF8.GetBytes(text));
}
