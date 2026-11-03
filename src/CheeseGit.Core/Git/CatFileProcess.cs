using System.Diagnostics;
using System.Text;

namespace CheeseGit.Core.Git;

/// <summary>An object read from the repository.</summary>
public sealed record GitObject(Oid Oid, string Type, byte[] Content);

/// <summary>
/// A long-lived <c>git cat-file --batch</c> process. Reading objects through it
/// costs 0.06 ms per commit versus 4.7 ms for a process per object (measured on
/// git/git in Phase 0); process start-up dominates, most of all on Windows.
/// Thread-safe: requests are serialized. The process is restarted if it dies.
/// </summary>
public sealed class CatFileProcess(GitRunner git, string repo) : IDisposable
{
    private readonly Lock gate = new();
    private Process? process;
    private Stream? stdin;
    private BufferedStream? stdout;
    private bool disposed;

    /// <summary>Reads one object by id, or null if it does not exist.</summary>
    public GitObject? Read(Oid oid) => this.Read(oid.ToHex());

    /// <summary>
    /// Reads an object by any revision expression git accepts (<c>HEAD:path</c>,
    /// <c>stash@{0}^</c>, an object id); null if it does not resolve. The
    /// expression must not contain a newline.
    /// </summary>
    public GitObject? Read(string revision)
    {
        if (revision.Contains('\n')) throw new ArgumentException("Revision must not contain a newline.", nameof(revision));
        return this.Read(Encoding.UTF8.GetBytes(revision));
    }

    /// <summary>
    /// Reads <c>&lt;treeish&gt;:&lt;path&gt;</c> with the path as raw bytes
    /// (it may not be valid UTF-8). Null if it does not exist.
    /// </summary>
    public GitObject? Read(string treeish, GitPath path)
    {
        if (path.Bytes.Contains((byte)'\n')) throw new ArgumentException("Path must not contain a newline.", nameof(path));
        return this.Read([.. Encoding.UTF8.GetBytes(treeish + ":"), .. path.Bytes]);
    }

    /// <summary>
    /// Reads an object whose content is kept only up to <paramref name="maxSize"/> bytes: a larger
    /// one comes back with its size and an empty content (a 500 MB asset must not be held in memory
    /// to show "too large"). Null if it does not exist.
    /// </summary>
    public (GitObject Object, long Size)? Read(Oid oid, long maxSize)
    {
        long size = 0;
        var read = this.Read(Encoding.ASCII.GetBytes(oid.ToHex()), maxSize, s => size = s);
        return read is null ? null : (read, size);
    }

    private GitObject? Read(byte[] revision, long maxSize = long.MaxValue, Action<long>? sized = null)
    {
        lock (this.gate)
        {
            ObjectDisposedException.ThrowIf(this.disposed, this);
            try
            {
                return this.ReadLocked(revision, maxSize, sized);
            }
            catch (IOException)
            {
                // The process died (e.g. killed by the OS); start a new one once.
                this.Stop();
                return this.ReadLocked(revision, maxSize, sized);
            }
        }
    }

    private GitObject? ReadLocked(byte[] revision, long maxSize, Action<long>? sized)
    {
        this.EnsureStarted();
        this.stdin!.Write(revision);
        this.stdin.WriteByte((byte)'\n');
        this.stdin.Flush();

        // "<oid> <type> <size>\n<content>\n", or "<input> missing\n" / "<input> ambiguous\n".
        var header = this.ReadLine();
        var parts = header.Split(' ');
        if (parts.Length != 3 || !long.TryParse(parts[2], out var size) || !Oid.TryParseHex(parts[0], out var oid)) return null;
        sized?.Invoke(size);
        byte[] content;
        if (size > maxSize)
        {
            content = [];
            var skip = new byte[1 << 16];
            for (var left = size; left > 0; left -= skip.Length) this.stdout!.ReadExactly(skip, 0, (int)Math.Min(left, skip.Length));
        }
        else
        {
            content = new byte[size];
            this.stdout!.ReadExactly(content);
        }
        if (this.stdout!.ReadByte() != '\n') throw new IOException("cat-file stream out of sync.");
        return new GitObject(oid, parts[1], content);
    }

    private void EnsureStarted()
    {
        if (this.process is not null && !this.process.HasExited) return;
        this.Stop();
        var psi = git.CreateStartInfo(repo, ["cat-file", "--batch"], GitAccess.Read);
        psi.RedirectStandardError = false;
        this.process = Process.Start(psi) ?? throw new IOException("Failed to start git cat-file.");
        this.stdin = this.process.StandardInput.BaseStream;
        this.stdout = new BufferedStream(this.process.StandardOutput.BaseStream, 1 << 16);
    }

    private string ReadLine()
    {
        var bytes = new List<byte>(96);
        int b;
        while ((b = this.stdout!.ReadByte()) != '\n')
        {
            if (b < 0) throw new IOException("cat-file closed its output.");
            bytes.Add((byte)b);
        }
        return Encoding.UTF8.GetString([.. bytes]);
    }

    private void Stop()
    {
        if (this.process is null) return;
        try
        {
            this.stdin?.Close();
            if (!this.process.WaitForExit(1000)) this.process.Kill();
        }
        catch (Exception e) when (e is IOException or InvalidOperationException)
        {
            // Already gone.
        }
        finally
        {
            this.process.Dispose();
            this.process = null;
            this.stdin = null;
            this.stdout = null;
        }
    }

    public void Dispose()
    {
        lock (this.gate)
        {
            this.disposed = true;
            this.Stop();
        }
    }
}
