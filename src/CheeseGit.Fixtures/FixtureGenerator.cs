using System.Text;
using CheeseGit.Core.Git;

namespace CheeseGit.Fixtures;

/// <summary>The real-world shapes the old app broke on (docs/testing.md, Fixtures).</summary>
public enum FixtureKind
{
    /// <summary>Unicode, quotes, spaces, newlines, leading dashes, non-UTF-8 (where the OS allows); every kind of working-tree change, including a file deleted while staged.</summary>
    HostilePaths,

    /// <summary>CRLF, LF, mixed and CR-only line endings, missing final newlines, Latin-1 and UTF-16 content, an eol attribute.</summary>
    LineEndings,

    /// <summary>Binary files, text/binary type changes, an executable, a symlink, an empty file.</summary>
    Binary,

    /// <summary>A submodule with a new commit and untracked content inside it.</summary>
    Submodule,

    /// <summary>A linked worktree (the fixture path), where .git is a file.</summary>
    LinkedWorktree,

    /// <summary>Git LFS pointer files and attributes, without git-lfs configured.</summary>
    LfsPointers,

    /// <summary>A shallow clone: history ends at grafted commits.</summary>
    Shallow,

    /// <summary>HEAD detached at an older commit.</summary>
    DetachedHead,

    /// <summary>Octopus and criss-cross merges, a second root, and branch names that clash with files, tags or options.</summary>
    MergeShapes,

    /// <summary>A merge paused on content, modify/delete and add/add conflicts.</summary>
    PausedMerge,

    /// <summary>1,000 branches, 200 tags and 300 remote-tracking branches.</summary>
    ManyRefs,

    /// <summary>A long, wide history (100k commits by default) of feature branches merging into main.</summary>
    LargeHistory,

    /// <summary>
    /// A wide working tree (100k files by default) after a sweeping change: every tenth file
    /// modified, a twentieth as many untracked, some deleted, a few staged, and a 50k-line
    /// generated file rewritten every fifth line (a huge, many-hunk diff).
    /// </summary>
    LargeWorktree,
}

public sealed record FixtureOptions
{
    public int LargeHistoryCommits { get; init; } = 100_000;

    public int ManyRefsBranches { get; init; } = 1_000;

    public int LargeWorktreeFiles { get; init; } = 100_000;
}

/// <summary>A generated repository. <see cref="Path"/> is the working tree to open.</summary>
public sealed record Fixture(FixtureKind Kind, string Path);

/// <summary>
/// Builds the fixture repositories deterministically: fixed identities and
/// dates, and an isolated git configuration, so a fixture is byte-identical
/// under every test profile. The profile's config applies only to the engine
/// that later reads it.
/// </summary>
public static class FixtureGenerator
{
    /// <summary>Bump when any fixture's content changes, so cached copies are rebuilt.</summary>
    public const int Version = 1;

    private const string Main = "refs/heads/main";

    public static async Task<Fixture> CreateAsync(FixtureKind kind, string directory, FixtureOptions? options = null, CancellationToken ct = default)
    {
        options ??= new FixtureOptions();
        Directory.CreateDirectory(directory);
        var emptyConfig = System.IO.Path.Combine(directory, ".fixture-gitconfig");
        await File.WriteAllTextAsync(emptyConfig, "", ct);
        var git = new FixtureGit(emptyConfig);
        var repo = System.IO.Path.Combine(directory, "repo");

        var path = kind switch
        {
            FixtureKind.HostilePaths => await HostilePathsAsync(git, repo, ct),
            FixtureKind.LineEndings => await LineEndingsAsync(git, repo, ct),
            FixtureKind.Binary => await BinaryAsync(git, repo, ct),
            FixtureKind.Submodule => await SubmoduleAsync(git, directory, repo, ct),
            FixtureKind.LinkedWorktree => await LinkedWorktreeAsync(git, directory, repo, ct),
            FixtureKind.LfsPointers => await LfsPointersAsync(git, repo, ct),
            FixtureKind.Shallow => await ShallowAsync(git, directory, repo, ct),
            FixtureKind.DetachedHead => await DetachedHeadAsync(git, repo, ct),
            FixtureKind.MergeShapes => await MergeShapesAsync(git, repo, ct),
            FixtureKind.PausedMerge => await PausedMergeAsync(git, repo, ct),
            FixtureKind.ManyRefs => await ManyRefsAsync(git, repo, options.ManyRefsBranches, ct),
            FixtureKind.LargeHistory => await LargeHistoryAsync(git, repo, options.LargeHistoryCommits, ct),
            FixtureKind.LargeWorktree => await LargeWorktreeAsync(git, repo, options.LargeWorktreeFiles, ct),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        return new Fixture(kind, path);
    }

    // ── Fixtures ──────────────────────────────────────────────────────

    private static async Task<string> HostilePathsAsync(FixtureGit git, string repo, CancellationToken ct)
    {
        var names = HostileNames.ForThisOs();
        var w = new FastImportWriter();
        var c1 = w.Commit(Main, "add hostile names", names.Select(n => new FileOp(n, Encoding.UTF8.GetBytes("v1\n"))).Append(FileOp.Write("README.md", "readme\n")));
        var c2 = w.Commit(Main, "touch every other name", names.Where((_, i) => i % 2 == 0).Select(n => new FileOp(n, Encoding.UTF8.GetBytes("v1\nv2\n"))), c1);
        // Branch names that look like a file, an option and a remote-tracking branch.
        w.Ref("refs/heads/README.md", c1);
        w.Ref("refs/heads/-dash", c1);
        w.Ref("refs/heads/origin/main", c2);
        await git.ImportAsync(repo, w, ct);

        // Working-tree state covering every status kind.
        for (var i = 0; i < names.Count; i++)
        {
            if (i % 3 != 0 && IsValidUtf8(names[i])) continue;
            // .NET cannot name a file that is not valid UTF-8; git can.
            if (IsValidUtf8(names[i])) await File.WriteAllTextAsync(FullPath(repo, names[i]), "changed\n", ct);
            else await git.ModifyInWorktreeAsync(repo, names[i], "changed\n"u8.ToArray(), ct);
        }
        await git.StageAsync(repo, [names[0]], ct);
        await File.WriteAllTextAsync(FullPath(repo, names[0]), "changed again after staging\n", ct);
        File.Delete(FullPath(repo, names[1]));
        await git.RunAsync(repo, ["mv", "--", Encoding.UTF8.GetString(names[2]), "renamed ✓.txt"], ct);

        var stagedThenDeleted = Encoding.UTF8.GetBytes("staged then deleted ✓.txt");
        await File.WriteAllTextAsync(FullPath(repo, stagedThenDeleted), "new\n", ct);
        await git.StageAsync(repo, [stagedThenDeleted], ct);
        File.Delete(FullPath(repo, stagedThenDeleted));

        foreach (var n in names.Take(4)) await File.WriteAllTextAsync(FullPath(repo, [.. "untracked/"u8, .. n]), "untracked\n", ct);
        return repo;
    }

    private static async Task<string> LineEndingsAsync(FixtureGit git, string repo, CancellationToken ct)
    {
        var w = new FastImportWriter();
        var c1 = w.Commit(Main, "line endings", [
            FileOp.Write(".gitattributes", "*.crlf text eol=crlf\n"),
            FileOp.Write("lf.txt", "one\ntwo\nthree\n"),
            FileOp.Write("crlf.txt", "one\r\ntwo\r\nthree\r\n"),
            FileOp.Write("mixed.txt", "one\r\ntwo\nthree\r\nfour\n"),
            FileOp.Write("cr-only.txt", "one\rtwo\rthree\r"),
            FileOp.Write("no-final-newline.txt", "one\ntwo"),
            FileOp.Write("attr.crlf", "one\ntwo\n"),
            FileOp.Write("latin1.txt", [.. "caf"u8, 0xE9, (byte)'\n', .. "na"u8, 0xEF, .. "ve\n"u8]),
            FileOp.Write("utf16.txt", [0xFF, 0xFE, .. Encoding.Unicode.GetBytes("hello\r\nworld\r\n")]),
            FileOp.Write("empty-lines.txt", "\n\n\n"),
        ]);
        w.Commit(Main, "edit with line endings preserved", [
            FileOp.Write("crlf.txt", "one\r\nTWO\r\nthree\r\n"),
            FileOp.Write("mixed.txt", "one\r\nTWO\nthree\r\nfour\n"),
            FileOp.Write("no-final-newline.txt", "one\ntwo\nthree"),
            FileOp.Write("latin1.txt", [.. "caf"u8, 0xE9, .. " cr"u8, 0xE8, .. "me\n"u8, .. "na"u8, 0xEF, .. "ve\n"u8]),
        ], c1);
        await git.ImportAsync(repo, w, ct);

        // Only the line endings change, and a final newline appears.
        await File.WriteAllTextAsync(System.IO.Path.Combine(repo, "lf.txt"), "one\r\ntwo\r\nthree\r\n", ct);
        await File.WriteAllTextAsync(System.IO.Path.Combine(repo, "no-final-newline.txt"), "one\ntwo\nthree\n", ct);
        await File.WriteAllTextAsync(System.IO.Path.Combine(repo, "empty-lines.txt"), "\n\nx\n\n", ct);
        return repo;
    }

    private static async Task<string> BinaryAsync(FixtureGit git, string repo, CancellationToken ct)
    {
        var random = new Random(7);
        byte[] Bytes(int n)
        {
            var b = new byte[n];
            random.NextBytes(b);
            return b;
        }

        var w = new FastImportWriter();
        // Not "nul.bin": NUL is a reserved device name on Windows, and git there refuses it even with an extension.
        var c1 = w.Commit(Main, "binary files", [
            FileOp.Write("image.png", [0x89, .. "PNG\r\n"u8, 0x1A, (byte)'\n', .. Bytes(2048)]),
            FileOp.Write("nul-byte.bin", [.. "text\0with nul\n"u8]),
            FileOp.Write("large.bin", Bytes(1 << 20)),
            FileOp.Write("becomes-binary.txt", "plain text\n"),
            FileOp.Write("becomes-text.dat", [0, 1, 2, 3]),
            FileOp.Write("empty", ""),
            new FileOp("run.sh"u8.ToArray(), "#!/bin/sh\necho hi\n"u8.ToArray(), "100755"),
            new FileOp("link"u8.ToArray(), "run.sh"u8.ToArray(), "120000"),
        ]);
        w.Commit(Main, "swap text and binary", [
            FileOp.Write("becomes-binary.txt", [0, 0, 0, 1]),
            FileOp.Write("becomes-text.dat", "now text\n"),
            FileOp.Write("image.png", [0x89, .. "PNG\r\n"u8, 0x1A, (byte)'\n', .. Bytes(2048)]),
            new FileOp("run.sh"u8.ToArray(), "#!/bin/sh\necho hi\n"u8.ToArray(), "100644"),
        ], c1);
        await git.ImportAsync(repo, w, ct);

        await File.WriteAllBytesAsync(System.IO.Path.Combine(repo, "nul-byte.bin"), [.. "changed\0\n"u8], ct);
        await File.WriteAllBytesAsync(System.IO.Path.Combine(repo, "untracked.bin"), Bytes(4096), ct);
        return repo;
    }

    private static async Task<string> SubmoduleAsync(FixtureGit git, string directory, string repo, CancellationToken ct)
    {
        var origin = System.IO.Path.Combine(directory, "sub-origin");
        var sw = new FastImportWriter();
        var s1 = sw.Commit(Main, "sub 1", [FileOp.Write("lib.txt", "lib v1\n")]);
        sw.Commit(Main, "sub 2", [FileOp.Write("lib.txt", "lib v2\n")], s1);
        await git.ImportAsync(origin, sw, ct);

        var w = new FastImportWriter();
        w.Commit(Main, "parent", [FileOp.Write("app.txt", "app\n")]);
        await git.ImportAsync(repo, w, ct);
        // Local file clones of submodules are blocked by default since git 2.38.1.
        await git.RunAsync(repo, ["-c", "protocol.file.allow=always", "submodule", "add", "-q", "--", origin, "libs/sub"], ct);
        await git.RunAsync(repo, ["commit", "-q", "-m", "add submodule"], ct);

        var sub = System.IO.Path.Combine(repo, "libs", "sub");
        await File.WriteAllTextAsync(System.IO.Path.Combine(sub, "lib.txt"), "lib v3\n", ct);
        await git.RunAsync(sub, ["commit", "-q", "-a", "-m", "sub 3"], ct);
        await File.WriteAllTextAsync(System.IO.Path.Combine(sub, "untracked-in-sub.txt"), "x\n", ct);
        await File.WriteAllTextAsync(System.IO.Path.Combine(repo, "app.txt"), "app changed\n", ct);
        return repo;
    }

    private static async Task<string> LinkedWorktreeAsync(FixtureGit git, string directory, string repo, CancellationToken ct)
    {
        var w = new FastImportWriter();
        var c1 = w.Commit(Main, "main 1", [FileOp.Write("file.txt", "one\n")]);
        w.Commit(Main, "main 2", [FileOp.Write("file.txt", "one\ntwo\n")], c1);
        await git.ImportAsync(repo, w, ct);

        var linked = System.IO.Path.Combine(directory, "linked");
        await git.RunAsync(repo, ["worktree", "add", "-q", "-b", "linked", "--", linked], ct);
        await File.WriteAllTextAsync(System.IO.Path.Combine(linked, "file.txt"), "changed in linked worktree\n", ct);
        await File.WriteAllTextAsync(System.IO.Path.Combine(linked, "new.txt"), "new\n", ct);
        return linked;
    }

    private static async Task<string> LfsPointersAsync(FixtureGit git, string repo, CancellationToken ct)
    {
        static string Pointer(char fill, int size) =>
            $"version https://git-lfs.github.com/spec/v1\noid sha256:{new string(fill, 64)}\nsize {size}\n";

        var w = new FastImportWriter();
        w.Commit(Main, "lfs pointers", [
            FileOp.Write(".gitattributes", "*.psd filter=lfs diff=lfs merge=lfs -text\n*.zip filter=lfs diff=lfs merge=lfs -text\n"),
            FileOp.Write("design.psd", Pointer('a', 123456)),
            FileOp.Write("assets.zip", Pointer('b', 987654321)),
            FileOp.Write("readme.txt", "uses lfs\n"),
        ]);
        await git.ImportAsync(repo, w, ct);
        await File.WriteAllTextAsync(System.IO.Path.Combine(repo, "design.psd"), Pointer('c', 222222), ct);
        return repo;
    }

    private static async Task<string> ShallowAsync(FixtureGit git, string directory, string repo, CancellationToken ct)
    {
        var origin = System.IO.Path.Combine(directory, "origin");
        var w = new FastImportWriter();
        var tip = w.Commit(Main, "commit 0", [FileOp.Write("file.txt", "0\n")]);
        for (var i = 1; i < 20; i++)
        {
            tip = w.Commit(Main, $"commit {i}", [FileOp.Write("file.txt", $"{i}\n")], tip);
            if (i == 12)
            {
                var side = w.Commit("refs/heads/side", "side", [FileOp.Write("side.txt", "side\n")], tip);
                tip = w.Commit(Main, "merge side", [], tip, side);
            }
        }
        await git.ImportAsync(origin, w, ct);
        // --depth needs a URL; a plain path would ignore it.
        await git.RunAsync(directory, ["clone", "-q", "--depth", "5", "--no-single-branch", "--", new Uri(origin).AbsoluteUri, repo], ct);
        return repo;
    }

    private static async Task<string> DetachedHeadAsync(FixtureGit git, string repo, CancellationToken ct)
    {
        var w = new FastImportWriter();
        var tip = w.Commit(Main, "commit 0", [FileOp.Write("file.txt", "0\n")]);
        var marks = new List<int> { tip };
        for (var i = 1; i < 6; i++) marks.Add(tip = w.Commit(Main, $"commit {i}", [FileOp.Write("file.txt", $"{i}\n")], tip));
        w.Commit("refs/heads/topic", "topic", [FileOp.Write("topic.txt", "topic\n")], marks[3]);
        await git.ImportAsync(repo, w, ct);
        await git.RunAsync(repo, ["switch", "-q", "--detach", "main~3"], ct);
        return repo;
    }

    private static async Task<string> MergeShapesAsync(FixtureGit git, string repo, CancellationToken ct)
    {
        var w = new FastImportWriter();
        FileOp F(string name) => FileOp.Write(name, name + " " + w.CommitCount + "\n");

        var m1 = w.Commit(Main, "main 1", [F("README.md")]);
        var m2 = w.Commit(Main, "main 2", [F("main.txt")], m1);

        // Octopus: three branches merged at once.
        var a = w.Commit("refs/heads/oct-a", "a", [F("a.txt")], m2);
        var b = w.Commit("refs/heads/oct-b", "b", [F("b.txt")], m2);
        var c = w.Commit("refs/heads/oct-c", "c", [F("c.txt")], m1);
        var m3 = w.Commit(Main, "octopus merge", [F("main.txt")], m2, a, b, c);

        // Criss-cross: two branches merge each other, then both reach main.
        var x1 = w.Commit("refs/heads/cross-x", "x1", [F("x.txt")], m3);
        var y1 = w.Commit("refs/heads/cross-y", "y1", [F("y.txt")], m3);
        var x2 = w.Commit("refs/heads/cross-x", "x2 merges y1", [], x1, y1);
        var y2 = w.Commit("refs/heads/cross-y", "y2 merges x1", [], y1, x1);
        var m4 = w.Commit(Main, "merge cross-x", [], m3, x2);
        var m5 = w.Commit(Main, "merge cross-y", [], m4, y2);

        // A second root (imported history) merged in, and a branch that keeps going after its merge.
        var r1 = w.Commit("refs/heads/imported", "imported root", [F("imported/one.txt")]);
        var r2 = w.Commit("refs/heads/imported", "imported 2", [F("imported/two.txt")], r1);
        var m6 = w.Commit(Main, "merge unrelated history", [], m5, r2);
        var t1 = w.Commit("refs/heads/topic", "topic 1", [F("topic.txt")], m4);
        var m7 = w.Commit(Main, "merge topic", [], m6, t1);
        w.Commit("refs/heads/topic", "topic after merge", [F("topic.txt")], t1);
        var m8 = w.Commit(Main, "main 8", [F("main.txt")], m7);

        // Names that clash: a branch and a tag both called v1, a branch named like a file.
        w.Ref("refs/heads/v1", m6);
        w.AnnotatedTag("v1", m3, "annotated v1");
        w.Ref("refs/tags/light", m8);
        w.Ref("refs/heads/main.txt", m2);
        await git.ImportAsync(repo, w, ct);
        return repo;
    }

    private static async Task<string> PausedMergeAsync(FixtureGit git, string repo, CancellationToken ct)
    {
        var w = new FastImportWriter();
        var b = w.Commit(Main, "base", [
            FileOp.Write("content.txt", "a\nb\nc\nd\ne\n"),
            FileOp.Write("crlf.txt", "a\r\nb\r\nc\r\n"),
            FileOp.Write("modify-delete.txt", "base\n"),
            FileOp.Write("clean.txt", "base\n"),
        ]);
        var theirs = w.Commit("refs/heads/theirs", "theirs", [
            FileOp.Write("content.txt", "a\nB-theirs\nc\nd\nE-theirs\n"),
            FileOp.Write("crlf.txt", "a\r\nB-theirs\r\nc\r\n"),
            FileOp.Delete("modify-delete.txt"),
            FileOp.Write("add-add.txt", "theirs\n"),
            FileOp.Write("clean.txt", "base\ntheirs\n"),
        ], b);
        w.Commit(Main, "ours", [
            FileOp.Write("content.txt", "a\nB-ours\nc\nd\nE-ours\n"),
            FileOp.Write("crlf.txt", "a\r\nB-ours\r\nc\r\n"),
            FileOp.Write("modify-delete.txt", "ours\n"),
            FileOp.Write("add-add.txt", "ours\n"),
        ], b);
        _ = theirs;
        await git.ImportAsync(repo, w, ct);
        var merge = await git.TryRunAsync(repo, ["merge", "--no-edit", "--end-of-options", "refs/heads/theirs"], ct);
        if (merge.ExitCode != 1) merge.EnsureSuccess("Start conflicting merge");
        return repo;
    }

    private static async Task<string> ManyRefsAsync(FixtureGit git, string repo, int branches, CancellationToken ct)
    {
        var w = new FastImportWriter();
        var marks = new List<int>();
        var tip = w.Commit(Main, "commit 0", [FileOp.Write("file0.txt", "0\n")]);
        marks.Add(tip);
        for (var i = 1; i < 2000; i++)
        {
            tip = w.Commit(Main, $"commit {i}", [FileOp.Write($"file{i % 50}.txt", $"{i}\n")], tip);
            marks.Add(tip);
        }
        var random = new Random(11);
        for (var i = 0; i < branches; i++)
        {
            var at = marks[random.Next(marks.Count)];
            // Every tenth branch has its own commit, so not all are on main.
            if (i % 10 == 0) at = w.Commit($"refs/heads/branch/{i:0000}", $"branch {i}", [FileOp.Write($"branch{i}.txt", "x\n")], at);
            else w.Ref($"refs/heads/branch/{i:0000}", at);
        }
        for (var i = 0; i < 200; i++)
        {
            var at = marks[i * 10];
            if (i % 2 == 0) w.AnnotatedTag($"v{i / 10}.{i % 10}.0", at, $"release {i}");
            else w.Ref($"refs/tags/light-{i}", at);
        }
        for (var i = 0; i < 300; i++) w.Ref($"refs/remotes/origin/remote/{i:000}", marks[random.Next(marks.Count)]);
        w.Ref("refs/remotes/origin/main", marks[^10]);
        await git.ImportAsync(repo, w, ct);
        await git.RunAsync(repo, ["remote", "add", "origin", "https://example.invalid/many-refs.git"], ct);
        await git.RunAsync(repo, ["symbolic-ref", "refs/remotes/origin/HEAD", "refs/remotes/origin/main"], ct);
        return repo;
    }

    /// <summary>
    /// Main with up to 24 concurrent feature branches that fork from main,
    /// commit a few times, sometimes merge main back in, and merge into main
    /// (with a merge commit). About 15% of commits are merges, as in busy
    /// real projects; a few branches stay unmerged at the end.
    /// </summary>
    private static async Task<string> LargeHistoryAsync(FixtureGit git, string repo, int commits, CancellationToken ct)
    {
        var random = new Random(42);
        var w = new FastImportWriter();
        var main = w.Commit(Main, "initial", [FileOp.Write("d00/f00.txt", "0\n")]);
        var lanes = new List<(int Id, int Tip, int Commits)>();
        var nextLane = 0;

        while (w.CommitCount < commits)
        {
            // 20 directories of 25 files: each commit rewrites two small trees
            // instead of one 500-entry tree (40 s → a few seconds for 100k).
            var n = random.Next(500);
            var file = $"d{n / 25:00}/f{n % 25:00}.txt";
            var op = new[] { FileOp.Write(file, $"{w.CommitCount}\n") };
            var roll = random.Next(100);
            if (lanes.Count < 24 && (lanes.Count == 0 || roll < 8))
            {
                var id = nextLane++;
                lanes.Add((id, w.Commit($"refs/heads/feature/{id}", $"start feature {id}", op, main), 1));
            }
            else if (roll < 30)
            {
                main = w.Commit(Main, $"main work {w.CommitCount}", op, main);
            }
            else
            {
                var i = random.Next(lanes.Count);
                var lane = lanes[i];
                if (lane.Commits >= 3 && random.Next(100) < 30)
                {
                    main = w.Commit(Main, $"Merge feature {lane.Id}", [], main, lane.Tip);
                    lanes.RemoveAt(i);
                }
                else if (lane.Commits >= 2 && random.Next(100) < 5)
                {
                    lanes[i] = lane with { Tip = w.Commit($"refs/heads/feature/{lane.Id}", "Merge main into feature", [], lane.Tip, main), Commits = lane.Commits + 1 };
                }
                else
                {
                    lanes[i] = lane with { Tip = w.Commit($"refs/heads/feature/{lane.Id}", $"feature {lane.Id} work", op, lane.Tip), Commits = lane.Commits + 1 };
                }
            }
        }
        await git.ImportAsync(repo, w, ct, deleteMergedBranches: true);
        return repo;
    }

    private static async Task<string> LargeWorktreeAsync(FixtureGit git, string repo, int files, CancellationToken ct)
    {
        // Three levels of 100-way fan-out at most, like a large source tree.
        static string FileName(int i) => $"src/m{i / 10_000:00}/p{i / 100 % 100:00}/file{i % 100:00}.cs";
        static string Content(int i, string extra) => $"namespace M{i / 10_000}.P{i / 100 % 100};\n\n// File {i}.{extra}\npublic static class C{i}\n{{\n    public static int Value => {i};\n}}\n";
        const int generatedLines = 50_000;
        static string Generated(bool changed) => string.Concat(Enumerable.Range(0, generatedLines).Select(i =>
            changed && i % 5 == 0 ? $"    public const int Value{i} = {i * 2}; // changed\n" : $"    public const int Value{i} = {i};\n"));

        var w = new FastImportWriter();
        var ops = Enumerable.Range(0, files).Select(i => FileOp.Write(FileName(i), Content(i, "")))
            .Append(FileOp.Write("generated/Constants.cs", Generated(changed: false)))
            .Append(FileOp.Write("README.md", "large worktree\n"));
        w.Commit(Main, "initial", ops);
        await git.ImportAsync(repo, w, ct);

        for (var i = 0; i < files; i += 10) await File.WriteAllTextAsync(System.IO.Path.Combine(repo, FileName(i)), Content(i, " Edited."), ct);
        for (var i = 5; i < files; i += 1000) File.Delete(System.IO.Path.Combine(repo, FileName(i)));
        for (var i = 0; i < files / 20; i++)
        {
            var path = System.IO.Path.Combine(repo, "untracked", $"u{i / 100:000}", $"new{i % 100:00}.cs");
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, Content(files + i, " New."), ct);
        }
        await File.WriteAllTextAsync(System.IO.Path.Combine(repo, "generated", "Constants.cs"), Generated(changed: true), ct);
        await git.StageAsync(repo, Enumerable.Range(0, Math.Min(files, 50)).Where(i => i % 10 == 0).Select(i => Encoding.UTF8.GetBytes(FileName(i))).ToList(), ct);
        return repo;
    }

    private static bool IsValidUtf8(byte[] bytes) => System.Text.Unicode.Utf8.IsValid(bytes);

    private static string FullPath(string repo, byte[] relative)
    {
        var full = System.IO.Path.Combine(repo, Encoding.UTF8.GetString(relative));
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        return full;
    }

    /// <summary>
    /// Runs git for fixture setup through <see cref="GitRunner"/> with a
    /// fixed identity and date and no user or system configuration.
    /// </summary>
    internal sealed class FixtureGit
    {
        private readonly GitRunner runner = new();

        public FixtureGit(string emptyConfig)
        {
            var env = this.runner.Environment;
            env["GIT_CONFIG_NOSYSTEM"] = "1";
            env["GIT_CONFIG_GLOBAL"] = emptyConfig;
            env["GIT_AUTHOR_NAME"] = env["GIT_COMMITTER_NAME"] = "Fixture";
            env["GIT_AUTHOR_EMAIL"] = env["GIT_COMMITTER_EMAIL"] = "fixture@example.com";
            env["GIT_AUTHOR_DATE"] = env["GIT_COMMITTER_DATE"] = "1700000000 +0000";
        }

        public Task<GitResult> TryRunAsync(string cwd, IReadOnlyList<string> args, CancellationToken ct, byte[]? stdin = null) =>
            this.runner.RunAsync(cwd, args, GitAccess.Write, stdin, ct);

        public async Task RunAsync(string cwd, IReadOnlyList<string> args, CancellationToken ct, byte[]? stdin = null) =>
            (await this.TryRunAsync(cwd, args, ct, stdin)).EnsureSuccess("Fixture setup: git " + args[0]);

        public Task StageAsync(string repo, IEnumerable<byte[]> paths, CancellationToken ct) =>
            this.RunAsync(repo, ["add", "--pathspec-from-file=-", "--pathspec-file-nul"], ct, GitPath.ToNulSeparated(paths.Select(p => GitPath.FromBytes(p))));

        /// <summary>
        /// Changes a tracked file in the working tree using only git and byte
        /// paths: stage the new content, check it out, restore the old index
        /// entry. Used for names the file APIs cannot express.
        /// </summary>
        public async Task ModifyInWorktreeAsync(string repo, byte[] path, byte[] newContent, CancellationToken ct)
        {
            // "<mode> <oid> <stage>\t<path>\0" for every index entry.
            var listing = (await this.TryRunAsync(repo, ["ls-files", "-s", "-z"], ct)).EnsureSuccess("ls-files").Stdout;
            var entry = SplitNul(listing).First(e => e.AsSpan(e.AsSpan().IndexOf((byte)'\t') + 1).SequenceEqual(path));
            var oldBlob = Encoding.ASCII.GetString(entry, 0, entry.AsSpan().IndexOf((byte)'\t')).Split(' ')[1];
            var newBlob = (await this.TryRunAsync(repo, ["hash-object", "-w", "--stdin"], ct, newContent)).EnsureSuccess("hash-object").StdoutText.Trim();
            byte[] Entry(string blob) => [.. Encoding.ASCII.GetBytes($"100644 {blob}\t"), .. path, 0];

            await this.RunAsync(repo, ["update-index", "-z", "--index-info"], ct, Entry(newBlob));
            await this.RunAsync(repo, ["checkout-index", "-f", "-z", "--stdin"], ct, [.. path, 0]);
            await this.RunAsync(repo, ["update-index", "-z", "--index-info"], ct, Entry(oldBlob));
        }

        private static List<byte[]> SplitNul(byte[] data)
        {
            var parts = new List<byte[]>();
            var start = 0;
            for (var i = 0; i <= data.Length; i++)
            {
                if (i == data.Length || data[i] == 0)
                {
                    parts.Add(data[start..i]);
                    start = i + 1;
                }
            }
            return parts;
        }

        /// <summary>Creates a repository on main from a fast-import stream and checks main out.</summary>
        public async Task ImportAsync(string repo, FastImportWriter writer, CancellationToken ct, bool deleteMergedBranches = false)
        {
            Directory.CreateDirectory(repo);
            await this.RunAsync(repo, ["init", "-q", "-b", "main"], ct);
            await this.RunAsync(repo, ["fast-import", "--quiet", "--date-format=raw"], ct, writer.ToBytes());
            if (deleteMergedBranches)
            {
                var merged = await this.TryRunAsync(repo, ["for-each-ref", "--merged", Main, "--format=delete %(refname)", "refs/heads/feature/"], ct);
                await this.RunAsync(repo, ["update-ref", "--stdin"], ct, merged.EnsureSuccess("List merged branches").Stdout);
            }
            await this.RunAsync(repo, ["reset", "-q", "--hard"], ct);
        }
    }
}
