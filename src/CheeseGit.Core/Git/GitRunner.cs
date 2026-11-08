using System.Buffers;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace CheeseGit.Core.Git;

/// <summary>
/// Whether a git invocation only reads repository state. Reads run with
/// <c>GIT_OPTIONAL_LOCKS=0</c> so they never take <c>index.lock</c> and never
/// race with user mutations.
/// </summary>
public enum GitAccess
{
    Read,
    Write,
}

/// <summary>Result of a fully buffered git invocation.</summary>
public sealed record GitResult(IReadOnlyList<string> Args, int ExitCode, byte[] Stdout, byte[] Stderr, TimeSpan Elapsed)
{
    public bool Success => this.ExitCode == 0;

    /// <summary>Stderr decoded for display. Never parse it: messages change between git versions and locales.</summary>
    public string StderrText => Encoding.UTF8.GetString(this.Stderr).Trim();

    public string StdoutText => Encoding.UTF8.GetString(this.Stdout);

    /// <summary>Throws <see cref="GitCommandException"/> unless git exited with 0.</summary>
    public GitResult EnsureSuccess(string operation)
    {
        if (!this.Success) throw new GitCommandException(operation, this);
        return this;
    }
}

/// <summary>Handles one stdout record (without its terminator).</summary>
public delegate void GitRecordHandler(ReadOnlySpan<byte> record);

/// <summary>
/// Runs the git CLI with an environment and config that make its output
/// independent of the user's locale and presentation settings, while still
/// honouring everything semantic (identity, hooks, credentials, filters).
/// </summary>
public sealed class GitRunner(string gitExecutable = "git")
{
    /// <summary>
    /// Presentation-only settings that would otherwise change the shape of the
    /// output we parse. Passed with <c>-c</c> so they override every config level.
    /// Each colour slot is listed because a specific slot (<c>color.diff=always</c>)
    /// wins over <c>color.ui</c>.
    /// </summary>
    private static readonly string[] HardenedConfig =
    [
        "color.ui=false",
        "color.diff=false",
        "color.status=false",
        "color.branch=false",
        "color.decorate=false",
        "color.interactive=false",
        "color.grep=false",
        "color.showBranch=false",
        "color.remote=false",
        "color.push=false",
        "color.transport=false",
        "color.advice=false",
        "column.ui=never",
        "core.quotepath=false",
        "diff.noprefix=false",
        "diff.mnemonicPrefix=false",
        "diff.relative=false",
        "diff.suppressBlankEmpty=false",
        "i18n.logOutputEncoding=UTF-8",
        "log.showSignature=false",
        "log.decorate=false",
        "status.relativePaths=false",
        "core.pager=cat",
        "advice.detachedHead=false",
    ];

    /// <summary>
    /// Variables a parent process may have set (for example when CheeseGit is
    /// started from a git hook) that would silently point git at another
    /// repository or index.
    /// </summary>
    private static readonly string[] ScrubbedVariables =
    [
        "GIT_DIR",
        "GIT_WORK_TREE",
        "GIT_INDEX_FILE",
        "GIT_OBJECT_DIRECTORY",
        "GIT_ALTERNATE_OBJECT_DIRECTORIES",
        "GIT_COMMON_DIR",
        "GIT_NAMESPACE",
        "GIT_PREFIX",
        "GIT_CONFIG",
        "GIT_EXTERNAL_DIFF",
        "GIT_DIFF_OPTS",
        "GIT_GLOB_PATHSPECS",
        "GIT_NOGLOB_PATHSPECS",
        "GIT_ICASE_PATHSPECS",
        "GIT_TRACE",
        "GIT_REFLOG_ACTION",
    ];

    private static readonly AsyncLocal<GitOperation?> CurrentOperation = new();
    private static long nextOperationId;

    public string GitExecutable { get; } = gitExecutable;

    /// <summary>Extra environment for every invocation (askpass bridge, tests).</summary>
    public Dictionary<string, string> Environment { get; } = [];

    /// <summary>Raised after every invocation; feeds the command log. May run on any thread.</summary>
    public event Action<GitInvocation>? Invoked;

    /// <summary>
    /// Labels every git invocation started inside the returned scope (on this
    /// async flow) with a new <see cref="GitOperation"/>, so the command log
    /// can group the calls of one user action. <paramref name="background"/>
    /// marks work the user did not ask for (refreshes), which the log collapses.
    /// </summary>
    public static GitOperationScope BeginOperation(OperationLabel label, bool background = false)
    {
        var previous = CurrentOperation.Value;
        var operation = new GitOperation(Interlocked.Increment(ref nextOperationId), label.Text, background, DateTimeOffset.Now) { Subjects = label.Subjects };
        CurrentOperation.Value = operation;
        return new GitOperationScope(operation, previous);
    }

    /// <summary>
    /// Raised when code marks an operation as failed (<see cref="GitOperationScope.Fail"/>).
    /// A non-zero exit code alone does not mean failure: many calls answer a
    /// question with it (<c>rev-parse --verify</c>, <c>diff --quiet</c>), so the
    /// command log takes the outcome from here. Static because operation scopes
    /// are ambient (async-local), not tied to a runner. May run on any thread.
    /// </summary>
    public static event Action<GitOperation, string>? OperationFailed;

    internal static void RestoreOperation(GitOperation? previous) => CurrentOperation.Value = previous;

    internal static void RaiseOperationFailed(GitOperation operation, string message) => OperationFailed?.Invoke(operation, message);

    /// <param name="literalPathspecs">
    /// False only for calls that pass a <c>:(glob)</c> pathspec built by
    /// <see cref="GitPath.ToPathspec"/> for a path that is not valid UTF-8.
    /// </param>
    public ProcessStartInfo CreateStartInfo(string workingDirectory, IReadOnlyList<string> args, GitAccess access, bool literalPathspecs = true)
    {
        var psi = new ProcessStartInfo(this.GitExecutable)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var cfg in HardenedConfig)
        {
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add(cfg);
        }
        foreach (var arg in args) psi.ArgumentList.Add(arg);

        var env = psi.Environment;
        foreach (var name in ScrubbedVariables) env.Remove(name);
        env["LC_ALL"] = "C";
        env["LANGUAGE"] = "C";
        env["GIT_TERMINAL_PROMPT"] = "0";
        env["GIT_PAGER"] = "cat";
        env["GIT_EDITOR"] = ":";
        env["GIT_SEQUENCE_EDITOR"] = ":";
        env["GIT_MERGE_AUTOEDIT"] = "no";
        // We never use glob pathspecs; a file literally named "*.txt" must
        // not stage every text file. check-ignore rejects the variable
        // ("pathspec magic not supported") and takes its paths literally anyway.
        if (literalPathspecs && (args.Count == 0 || args[0] != "check-ignore")) env["GIT_LITERAL_PATHSPECS"] = "1";
        if (access == GitAccess.Read) env["GIT_OPTIONAL_LOCKS"] = "0";
        foreach (var (key, value) in this.Environment) env[key] = value;
        return psi;
    }

    /// <summary>Runs git and buffers stdout and stderr.</summary>
    public async Task<GitResult> RunAsync(
        string workingDirectory,
        IReadOnlyList<string> args,
        GitAccess access = GitAccess.Read,
        ReadOnlyMemory<byte> stdin = default,
        CancellationToken ct = default,
        bool literalPathspecs = true,
        Action<string>? stderrLine = null)
    {
        var started = DateTimeOffset.Now;
        var sw = Stopwatch.StartNew();
        using var proc = this.Start(workingDirectory, args, access, literalPathspecs, wideProgress: stderrLine is not null);
        using var reg = ct.Register(() => TryKill(proc));

        var stdoutTask = ReadAllAsync(proc.StandardOutput.BaseStream, ct);
        var stderrTask = stderrLine is null
            ? ReadAllAsync(proc.StandardError.BaseStream, ct)
            : ReadLinesAsync(proc.StandardError.BaseStream, stderrLine, ct);
        await WriteStdinAsync(proc, stdin, ct);
        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        await proc.WaitForExitAsync(ct);

        var result = new GitResult(args, proc.ExitCode, stdout, stderr, sw.Elapsed);
        this.Invoked?.Invoke(new GitInvocation(workingDirectory, args, access, result.ExitCode, started, sw.Elapsed, stderr, CurrentOperation.Value));
        return result;
    }

    /// <summary>Runs git and throws <see cref="GitCommandException"/> on a non-zero exit code.</summary>
    public async Task<GitResult> RunCheckedAsync(
        string operation,
        string workingDirectory,
        IReadOnlyList<string> args,
        GitAccess access = GitAccess.Read,
        ReadOnlyMemory<byte> stdin = default,
        CancellationToken ct = default,
        bool literalPathspecs = true,
        Action<string>? stderrLine = null)
    {
        var result = await this.RunAsync(workingDirectory, args, access, stdin, ct, literalPathspecs, stderrLine);
        return result.EnsureSuccess(operation);
    }

    /// <summary>
    /// Runs git and hands stdout to <paramref name="handler"/> one record at a
    /// time as it arrives, without buffering the whole output. Records are
    /// separated by <paramref name="separator"/> (use 0 with <c>-z</c> formats).
    /// The handler runs on a background thread.
    /// </summary>
    public async Task<GitResult> StreamRecordsAsync(
        string workingDirectory,
        IReadOnlyList<string> args,
        byte separator,
        GitRecordHandler handler,
        GitAccess access = GitAccess.Read,
        ReadOnlyMemory<byte> stdin = default,
        CancellationToken ct = default,
        bool literalPathspecs = true)
    {
        var started = DateTimeOffset.Now;
        var sw = Stopwatch.StartNew();
        using var proc = this.Start(workingDirectory, args, access, literalPathspecs);
        using var reg = ct.Register(() => TryKill(proc));

        var stderrTask = ReadAllAsync(proc.StandardError.BaseStream, ct);
        await WriteStdinAsync(proc, stdin, ct);
        await Task.Run(() => SplitRecords(proc.StandardOutput.BaseStream, separator, handler), ct);
        var stderr = await stderrTask;
        await proc.WaitForExitAsync(ct);

        this.Invoked?.Invoke(new GitInvocation(workingDirectory, args, access, proc.ExitCode, started, sw.Elapsed, stderr, CurrentOperation.Value));
        return new GitResult(args, proc.ExitCode, [], stderr, sw.Elapsed);
    }

    /// <param name="wideProgress">
    /// git splits a progress meter wider than the terminal (80 columns without one) into a title
    /// line and a counter line, which leaves the counter without its phase.
    /// </param>
    private Process Start(string workingDirectory, IReadOnlyList<string> args, GitAccess access, bool literalPathspecs = true, bool wideProgress = false)
    {
        var psi = this.CreateStartInfo(workingDirectory, args, access, literalPathspecs);
        if (wideProgress) psi.Environment["COLUMNS"] = "1000";
        var proc = new Process { StartInfo = psi };
        try
        {
            proc.Start();
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            proc.Dispose();
            throw new GitNotFoundException(this.GitExecutable, e);
        }
        return proc;
    }

    private static void SplitRecords(Stream stream, byte separator, GitRecordHandler handler)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(1 << 16);
        try
        {
            var filled = 0;
            while (true)
            {
                if (filled == buffer.Length)
                {
                    // A single record larger than the buffer: grow.
                    var bigger = ArrayPool<byte>.Shared.Rent(buffer.Length * 2);
                    buffer.AsSpan(0, filled).CopyTo(bigger);
                    ArrayPool<byte>.Shared.Return(buffer);
                    buffer = bigger;
                }
                var read = stream.Read(buffer, filled, buffer.Length - filled);
                if (read == 0) break;
                var start = 0;
                var end = filled + read;
                for (var i = filled; i < end; i++)
                {
                    if (buffer[i] != separator) continue;
                    handler(buffer.AsSpan(start, i - start));
                    start = i + 1;
                }
                filled = end - start;
                buffer.AsSpan(start, filled).CopyTo(buffer);
            }
            if (filled > 0) handler(buffer.AsSpan(0, filled));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static async Task WriteStdinAsync(Process proc, ReadOnlyMemory<byte> stdin, CancellationToken ct)
    {
        try
        {
            if (!stdin.IsEmpty) await proc.StandardInput.BaseStream.WriteAsync(stdin, ct);
        }
        catch (IOException)
        {
            // git exited before reading all of stdin (e.g. a usage error); the
            // exit code reports the failure.
        }
        finally
        {
            try
            {
                proc.StandardInput.Close();
            }
            catch (IOException)
            {
                // Same as above: the pipe is already closed.
            }
        }
    }

    private static async Task<byte[]> ReadAllAsync(Stream stream, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, ct);
        return ms.ToArray();
    }

    /// <summary>
    /// Reads stderr while handing each line to <paramref name="onLine"/> as it arrives. Progress
    /// meters (<c>--progress</c>) redraw their line with <c>\r</c>, so <c>\r</c> ends a line too.
    /// The returned bytes keep only the last state of each redrawn line: a fetch of git/git redraws
    /// "Receiving objects" hundreds of times, and the command log and error messages want the result.
    /// </summary>
    private static async Task<byte[]> ReadLinesAsync(Stream stream, Action<string> onLine, CancellationToken ct)
    {
        using var kept = new MemoryStream();
        var line = new List<byte>(256);
        var buffer = new byte[4096];
        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            for (var i = 0; i < read; i++)
            {
                var b = buffer[i];
                if (b is not ((byte)'\r' or (byte)'\n'))
                {
                    line.Add(b);
                    continue;
                }
                if (line.Count > 0) onLine(Encoding.UTF8.GetString(CollectionsMarshal.AsSpan(line)));
                if (b == (byte)'\n')
                {
                    kept.Write(CollectionsMarshal.AsSpan(line));
                    kept.WriteByte((byte)'\n');
                }
                // A redrawn line is dropped from what is kept unless a newline ends it.
                line.Clear();
            }
        }
        if (line.Count > 0)
        {
            onLine(Encoding.UTF8.GetString(CollectionsMarshal.AsSpan(line)));
            kept.Write(CollectionsMarshal.AsSpan(line));
        }
        return kept.ToArray();
    }

    private static void TryKill(Process proc)
    {
        try
        {
            proc.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Already exited.
        }
    }

}

/// <summary>The scope of one <see cref="GitOperation"/>; disposing restores the enclosing one.</summary>
public sealed class GitOperationScope : IDisposable
{
    private readonly GitOperation? previous;
    private int failed;

    internal GitOperationScope(GitOperation operation, GitOperation? previous)
    {
        this.Operation = operation;
        this.previous = previous;
    }

    public GitOperation Operation { get; }

    /// <summary>Reports that the operation failed. Cancellation is not a failure; repeated calls report once.</summary>
    public void Fail(Exception error)
    {
        if (error is OperationCanceledException || Interlocked.Exchange(ref this.failed, 1) != 0) return;
        GitRunner.RaiseOperationFailed(this.Operation, error.Message);
    }

    public void Dispose() => GitRunner.RestoreOperation(this.previous);
}

/// <summary>One user action or background task; every git call it makes carries it.</summary>
/// <param name="Id">Unique per process, so two consecutive "Stage" actions stay apart in the log.</param>
public sealed record GitOperation(long Id, string Label, bool Background, DateTimeOffset Started)
{
    /// <summary>The private names in <see cref="Label"/> (<see cref="OperationLabel.Subjects"/>).</summary>
    public IReadOnlyList<string> Subjects { get; init; } = [];
}

/// <summary>One completed git invocation, for the command log.</summary>
/// <param name="Operation">The enclosing <see cref="GitRunner.BeginOperation"/> scope, or null.</param>
public sealed record GitInvocation(
    string WorkingDirectory,
    IReadOnlyList<string> Args,
    GitAccess Access,
    int ExitCode,
    DateTimeOffset Started,
    TimeSpan Elapsed,
    byte[] Stderr,
    GitOperation? Operation);
