using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using CheeseGit.App.ViewModels;

namespace CheeseGit.App.Services;

/// <summary>
/// What a problem report holds: the window as a PNG, the command log and the
/// environment. Saved as one .zip, which an issue can take as an attachment.
/// </summary>
public sealed record ProblemReport(byte[]? Screenshot, string Log, string Environment)
{
    public const string ScreenshotName = "screenshot.png";
    public const string LogName = "command-log.txt";
    public const string EnvironmentName = "environment.txt";

    public byte[] ToZip()
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            if (this.Screenshot is { } png) Add(zip, ScreenshotName, png);
            Add(zip, LogName, Encoding.UTF8.GetBytes(this.Log));
            Add(zip, EnvironmentName, Encoding.UTF8.GetBytes(this.Environment));
        }
        return buffer.ToArray();
    }

    private static void Add(ZipArchive zip, string name, byte[] content)
    {
        using var stream = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
        stream.Write(content);
    }

    /// <summary>
    /// The app, the system and each open repository's shape. No names or paths:
    /// counts and settings only, so this part needs no anonymizing.
    /// </summary>
    public static string EnvironmentText(MainViewModel main, bool anonymized)
    {
        var s = main.Settings;
        var sb = new StringBuilder();
        sb.Append($"CheeseGit {main.AppVersion}\n");
        sb.Append($"OS: {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})\n");
        sb.Append($"Runtime: {RuntimeInformation.FrameworkDescription}\n");
        if (OperatingSystem.IsLinux())
        {
            var session = System.Environment.GetEnvironmentVariable("XDG_SESSION_TYPE") ?? "unknown";
            sb.Append($"Session: {session}, native Wayland {(Program.NativeWayland ? "on" : "off")}\n");
        }
        sb.Append($"Theme: {s.Theme}; diff view: {s.DiffViewMode}; changes on switch: {s.SwitchChanges}; LFS previews up to {s.LfsAutoDownloadMegabytes} MB\n");
        sb.Append($"Anonymized: {(anonymized ? "yes" : "no")}\n");
        sb.Append($"Open repositories: {main.Tabs.Count}\n");
        for (var i = 0; i < main.Tabs.Count; i++)
        {
            var tab = main.Tabs[i];
            sb.Append($"  #{i + 1}{(tab == main.SelectedTab ? " (shown)" : "")}: ");
            if (tab.OpenError is not null)
            {
                sb.Append("could not be opened\n");
                continue;
            }
            if (tab.Session is not { } session || tab.Snapshot is not { } snapshot)
            {
                sb.Append("opening\n");
                continue;
            }
            sb.Append($"git {session.Repository.Version}, {(session.IsWatching ? "watching" : "polling")}");
            sb.Append($", {snapshot.Status.Entries.Count} changed files, {snapshot.Refs.Count} refs, {snapshot.Remotes.Count} remotes, {snapshot.Stashes.Count} stashes");
            sb.Append($", {tab.History.Rows.Count} commits loaded");
            if (snapshot.Head.BrowsingHistory) sb.Append(", detached HEAD");
            if (snapshot.Operation.Kind != Core.Repository.OperationKind.None) sb.Append($", {snapshot.Operation.Kind} in progress ({snapshot.Operation.ConflictCount} conflicts)");
            sb.Append($", left panel: {tab.LeftTab}\n");
        }
        return sb.ToString();
    }
}
