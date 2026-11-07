using System.Diagnostics;
using CheeseGit.Core.Git;
using CheeseGit.Core.Repository;

namespace CheeseGit.Core.Platform;

/// <summary>The files of one conflicted path handed to a merge tool.</summary>
/// <param name="Local">Our side (index stage 2), in a temp folder.</param>
/// <param name="Remote">Their side (stage 3), in a temp folder.</param>
/// <param name="Base">The merge base (stage 1), in a temp folder; an empty file when <paramref name="HasBase"/> is false.</param>
/// <param name="Merged">The working-tree file the tool writes the result to.</param>
/// <param name="HasBase">
/// Whether a common version exists. A file added on both sides (common after a squash merge) has
/// none. Given an empty base, a three-way tool sees every line as added on both sides: VS Code
/// showed the whole file as one conflict with an empty result, when git's markers hold only the
/// lines that differ. So those tools get the same call <c>git mergetool</c> makes without a base.
/// </param>
public sealed record MergeToolFiles(string Local, string Remote, string Base, string Merged, bool HasBase);

/// <summary>
/// An external merge tool (old app's merge_tools registry). Each opens a three-way merge with the
/// arguments git's own <c>mergetool</c> uses for it; the old app opened the file with conflict
/// markers instead (and gave VS Code the same file four times), which is now only VS Code's way
/// when there is no base. The result lands in the working tree, where the conflict dialog sees the
/// markers go away.
/// </summary>
/// <param name="Executables">Names searched on PATH, first found wins.</param>
/// <param name="InTerminal">A terminal program (vim): started inside a terminal emulator.</param>
public sealed record MergeTool(
    string Id,
    string DisplayName,
    string? Icon,
    IReadOnlyList<string> Executables,
    Func<MergeToolFiles, IReadOnlyList<string>> Arguments,
    bool InTerminal = false);

/// <summary>Finds the merge tools installed on this machine and starts them.</summary>
public sealed class MergeToolRegistry
{
    /// <summary>Terminal emulators tried in order on Linux, with the argument that starts a command.</summary>
    private static readonly (string Name, string[] Prefix)[] LinuxTerminals =
    [
        ("ptyxis", ["--"]),
        ("gnome-terminal", ["--"]),
        ("konsole", ["-e"]),
        ("xfce4-terminal", ["-x"]),
        ("alacritty", ["-e"]),
        ("kitty", ["--"]),
        ("wezterm", ["start", "--"]),
        ("foot", ["--"]),
        ("xterm", ["-e"]),
        ("x-terminal-emulator", ["-e"]),
    ];

    private readonly Func<string, string?> findExecutable;
    private readonly Action<ProcessStartInfo> start;

    /// <param name="findExecutable">Looks a program up on PATH (tests pass their own).</param>
    /// <param name="start">Starts a process without waiting for it (tests record instead).</param>
    public MergeToolRegistry(IReadOnlyList<MergeTool>? tools = null, Func<string, string?>? findExecutable = null, Action<ProcessStartInfo>? start = null)
    {
        this.All = tools ?? KnownTools;
        this.findExecutable = findExecutable ?? FindOnPath;
        this.start = start ?? (psi => Process.Start(psi)?.Dispose());
    }

    public IReadOnlyList<MergeTool> All { get; }

    /// <summary>The tools whose program is on PATH (and, for terminal tools, a terminal to run them in).</summary>
    public IReadOnlyList<MergeTool> Available() =>
        [.. this.All.Where(t => this.Resolve(t) is not null && (!t.InTerminal || this.TerminalPrefix() is not null))];

    /// <summary>The preferred tool when it is available, else the first available one.</summary>
    public MergeTool? Pick(string? preferredId)
    {
        var available = this.Available();
        return available.FirstOrDefault(t => t.Id == preferredId) ?? available.FirstOrDefault();
    }

    /// <summary>
    /// Writes the three versions of a conflicted file to a temp folder and starts the tool on
    /// them; returns without waiting. The temp files stay (the tool may still read them): they
    /// are small, and the OS clears its temp folder.
    /// </summary>
    public async Task<ProcessStartInfo> OpenAsync(MergeTool tool, GitRepository repo, GitPath path, CancellationToken ct = default)
    {
        var program = this.Resolve(tool) ?? throw new GitOperationException(GitErrorCode.NotFound, $"{tool.DisplayName} is not installed (none of {string.Join(", ", tool.Executables)} is on PATH).");
        var stages = await repo.ConflictStagesAsync(path, ct);
        var folder = Directory.CreateTempSubdirectory("cheesegit-merge-").FullName;
        var name = Path.GetFileNameWithoutExtension(path.Name);
        var extension = Path.GetExtension(path.Name);
        string Write(string side, byte[]? content)
        {
            // Keep the extension so the tool picks the right syntax highlighting.
            var file = Path.Combine(folder, $"{name}.{side}{extension}");
            File.WriteAllBytes(file, content ?? []);
            return file;
        }
        var files = new MergeToolFiles(Write("LOCAL", stages.Ours), Write("REMOTE", stages.Theirs), Write("BASE", stages.Base), path.ToFullPath(repo.Root), stages.Base is not null);

        ProcessStartInfo psi;
        if (tool.InTerminal)
        {
            psi = this.TerminalStartInfo(program, tool.Arguments(files));
        }
        else
        {
            psi = new ProcessStartInfo(program);
            foreach (var arg in tool.Arguments(files)) psi.ArgumentList.Add(arg);
        }
        psi.UseShellExecute = false;
        psi.WorkingDirectory = repo.Root;
        try
        {
            this.start(psi);
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            throw new GitOperationException(GitErrorCode.NotFound, $"Could not start {tool.DisplayName}: {e.Message}", e);
        }
        return psi;
    }

    private string? Resolve(MergeTool tool) => tool.Executables.Select(this.findExecutable).FirstOrDefault(p => p is not null);

    private (string Program, string[] Prefix)? TerminalPrefix()
    {
        if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()) return ("", []);
        foreach (var (name, prefix) in LinuxTerminals)
        {
            if (this.findExecutable(name) is { } program) return (program, prefix);
        }
        return null;
    }

    private ProcessStartInfo TerminalStartInfo(string program, IReadOnlyList<string> args)
    {
        ProcessStartInfo psi;
        if (OperatingSystem.IsWindows())
        {
            // "start" opens a new console window; the empty string is its title.
            psi = new ProcessStartInfo("cmd.exe");
            foreach (var a in (string[])["/c", "start", "", program, .. args]) psi.ArgumentList.Add(a);
            return psi;
        }
        if (OperatingSystem.IsMacOS())
        {
            // Terminal.app runs a command file; build one that execs the tool.
            var script = Path.Combine(Directory.CreateTempSubdirectory("cheesegit-merge-").FullName, "merge.command");
            File.WriteAllText(script, "#!/bin/sh\nexec " + string.Join(' ', ((string[])[program, .. args]).Select(ShellQuote)) + "\n");
            File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            psi = new ProcessStartInfo("open");
            foreach (var a in (string[])["-a", "Terminal", script]) psi.ArgumentList.Add(a);
            return psi;
        }
        var (terminal, prefix) = this.TerminalPrefix() ?? throw new GitOperationException(GitErrorCode.NotFound, "No terminal emulator found to run the merge tool in.");
        psi = new ProcessStartInfo(terminal);
        foreach (var a in (string[])[.. prefix, program, .. args]) psi.ArgumentList.Add(a);
        return psi;
    }

    private static string ShellQuote(string s) => "'" + s.Replace("'", "'\\''") + "'";

    /// <summary>Looks <paramref name="name"/> up on PATH (with PATHEXT on Windows) without starting anything.</summary>
    public static string? FindOnPath(string name)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        string[] extensions = OperatingSystem.IsWindows()
            ? [.. (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT").Split(';', StringSplitOptions.RemoveEmptyEntries)]
            : [""];
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var ext in extensions)
            {
                var candidate = Path.Combine(dir, name + ext);
                if (!File.Exists(candidate)) continue;
                if (OperatingSystem.IsWindows() || (File.GetUnixFileMode(candidate) & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0)
                {
                    return candidate;
                }
            }
        }
        return null;
    }

    private static IReadOnlyList<string> VimArguments(MergeToolFiles f) =>
        f.HasBase ? ["-f", "-d", "-c", "wincmd J", f.Merged, f.Local, f.Base, f.Remote] : ["-f", "-d", "-c", "wincmd J", f.Merged, f.Local, f.Remote];

    private static IReadOnlyList<string> JetBrainsArguments(MergeToolFiles f) =>
        f.HasBase ? ["merge", f.Local, f.Remote, f.Base, f.Merged] : ["merge", f.Local, f.Remote, f.Merged];

    /// <summary>The old app's tools, in its order. Arguments follow git's <c>mergetools/</c> scripts.</summary>
    public static readonly IReadOnlyList<MergeTool> KnownTools =
    [
        new("vscode", "Visual Studio Code", "vscode.svg", ["code"], f => f.HasBase ? ["--wait", "--merge", f.Local, f.Remote, f.Base, f.Merged] : [f.Merged]),
        new("neovim", "NeoVim", "neovim.svg", ["nvim"], VimArguments, InTerminal: true),
        new("vim", "Vim", "vim.svg", ["vim"], VimArguments, InTerminal: true),
        new("rider", "JetBrains Rider", "rider.svg", ["rider", "rider.sh", "rider64"], JetBrainsArguments),
        new("intellij", "IntelliJ IDEA", "intellij.svg", ["idea", "idea.sh", "idea64"], JetBrainsArguments),
        new("kdiff3", "KDiff3", null, ["kdiff3"], f => f.HasBase
            ? ["--L1", "Base", "--L2", "Local", "--L3", "Remote", "-o", f.Merged, f.Base, f.Local, f.Remote]
            : ["--L1", "Local", "--L2", "Remote", "-o", f.Merged, f.Local, f.Remote]),
        new("meld", "Meld", null, ["meld"], f => ["--output", f.Merged, f.Local, f.HasBase ? f.Base : f.Merged, f.Remote]),
        new("beyondcompare", "Beyond Compare", null, ["bcomp", "bcompare"], f => f.HasBase ? [f.Local, f.Remote, f.Base, "-mergeoutput=" + f.Merged] : [f.Local, f.Remote, "-mergeoutput=" + f.Merged]),
        new("visualstudio", "Visual Studio", "visualstudio.svg", OperatingSystem.IsWindows() ? ["vsdiffmerge"] : [], f => [f.Remote, f.Local, f.Base, f.Merged, "/m"]),
    ];
}
