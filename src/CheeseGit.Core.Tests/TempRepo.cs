using System.Diagnostics;
using System.Text;
using CheeseGit.Core.Git;

namespace CheeseGit.Core.Tests;

/// <summary>
/// A throwaway directory, usually holding a repository. Setup goes through
/// <see cref="Raw"/> (plain git, the active test profile's config); the code
/// under test goes through the hardened <see cref="GitRunner"/>.
/// </summary>
public sealed class TempRepo : IDisposable
{
    private TempRepo(string path)
    {
        this.Path = path;
    }

    public string Path { get; }

    public GitRunner Git { get; } = new();

    /// <summary>An empty directory (not a repository).</summary>
    public static TempRepo EmptyDirectory()
    {
        TestEnvironment.EnsureInitialized();
        return new TempRepo(Directory.CreateTempSubdirectory("cheesegit-test-").FullName);
    }

    /// <summary>A fresh repository with no commits, on the profile's default branch.</summary>
    public static TempRepo Init()
    {
        var repo = EmptyDirectory();
        repo.Raw("init", "-q");
        return repo;
    }

    /// <summary>A repository with one commit holding hello.txt (four lines), like the Rust suite's fixture.</summary>
    public static TempRepo WithCommit()
    {
        var repo = Init();
        repo.CommitFile("hello.txt", "line1\nline2\nline3\nline4\n", "initial commit");
        return repo;
    }

    /// <summary>A repository with <paramref name="count"/> commits, each adding file{i}.txt.</summary>
    public static TempRepo WithCommits(int count)
    {
        var repo = Init();
        for (var i = 0; i < count; i++) repo.CommitFile($"file{i}.txt", $"content {i}\n", $"commit {i}");
        return repo;
    }

    public static TempRepo InitBare()
    {
        var repo = EmptyDirectory();
        repo.Raw("init", "-q", "--bare");
        return repo;
    }

    public static TempRepo CloneOf(TempRepo remote)
    {
        var repo = EmptyDirectory();
        repo.Raw("clone", "-q", remote.Path, repo.Path);
        return repo;
    }

    /// <summary>Opens this repository through the code under test.</summary>
    public Task<Repository.GitRepository> OpenAsync() => Repository.GitRepository.OpenAsync(this.Path, this.Git);

    public string FullPath(string relativePath) => System.IO.Path.Combine(this.Path, relativePath);

    public void Write(string relativePath, string content) => this.Write(relativePath, Encoding.UTF8.GetBytes(content));

    public void Write(string relativePath, byte[] content)
    {
        var full = this.FullPath(relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, content);
    }

    public string Read(string relativePath) => File.ReadAllText(this.FullPath(relativePath));

    /// <summary>File content with CRLF folded to LF, for profiles that check out with core.autocrlf=true.</summary>
    public string ReadNormalized(string relativePath) => this.Read(relativePath).Replace("\r\n", "\n");

    public bool Exists(string relativePath) => File.Exists(this.FullPath(relativePath));

    public void Delete(string relativePath) => File.Delete(this.FullPath(relativePath));

    /// <summary>Writes, stages and commits one file; returns the new HEAD.</summary>
    public string CommitFile(string relativePath, string content, string message)
    {
        this.Write(relativePath, content);
        this.Raw("add", "--", relativePath);
        this.Raw("commit", "-q", "-m", message);
        return this.Head();
    }

    public void CommitAll(string message)
    {
        this.Raw("add", "-A");
        this.Raw("commit", "-q", "-m", message);
    }

    public string Head() => this.Raw("rev-parse", "HEAD").Trim();

    public string CurrentBranch() => this.Raw("rev-parse", "--abbrev-ref", "HEAD").Trim();

    /// <summary>Runs git without the runner's hardening (test setup only); throws on failure.</summary>
    public string Raw(params string[] args)
    {
        var (exit, stdout, stderr) = this.TryRaw(args);
        if (exit != 0) throw new InvalidOperationException($"git {string.Join(' ', args)} failed ({exit}): {stderr}");
        return stdout;
    }

    public (int ExitCode, string Stdout, string Stderr) TryRaw(params string[] args) => this.TryRaw(null, args);

    /// <summary>
    /// Variant with extra environment for this call only. Tests run in
    /// parallel, so they must never set process-wide variables.
    /// </summary>
    /// <summary>Runs git and returns stdout as bytes (blob contents); throws on failure.</summary>
    public byte[] RawBytes(params string[] args)
    {
        var psi = new ProcessStartInfo("git") { WorkingDirectory = this.Path, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var stderrTask = p.StandardError.ReadToEndAsync();
        using var stdout = new MemoryStream();
        p.StandardOutput.BaseStream.CopyTo(stdout);
        p.WaitForExit();
        if (p.ExitCode != 0) throw new InvalidOperationException($"git {string.Join(' ', args)} failed ({p.ExitCode}): {stderrTask.Result}");
        return stdout.ToArray();
    }

    public string RawWithEnv(IReadOnlyDictionary<string, string> env, params string[] args)
    {
        var (exit, stdout, stderr) = this.TryRaw(env, args);
        if (exit != 0) throw new InvalidOperationException($"git {string.Join(' ', args)} failed ({exit}): {stderr}");
        return stdout;
    }

    private (int ExitCode, string Stdout, string Stderr) TryRaw(IReadOnlyDictionary<string, string>? env, string[] args)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = this.Path,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            // git writes names as UTF-8 bytes; the default (the console code page on Windows) read "feature/ü" as "feature/├╝".
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        // Setup output is parsed by tests (rev-parse, branch names), so it
        // must not be coloured even under the hostile profile.
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add("color.ui=false");
        foreach (var a in args) psi.ArgumentList.Add(a);
        psi.Environment["GIT_EDITOR"] = ":";
        psi.Environment["GIT_MERGE_AUTOEDIT"] = "no";
        if (env is not null)
        {
            foreach (var (k, v) in env) psi.Environment[k] = v;
        }
        using var p = Process.Start(psi)!;
        p.StandardInput.Close();
        var stderrTask = p.StandardError.ReadToEndAsync();
        var stdout = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, stdout, stderrTask.Result);
    }

    public void Dispose()
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(this.Path, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(this.Path, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Best effort.
        }
    }
}
