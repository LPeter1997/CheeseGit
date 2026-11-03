using System.Diagnostics;
using Avalonia;
using CheeseGit.App.Services;
using Velopack;

namespace CheeseGit.App;

internal static class Program
{
    /// <summary>Started before anything else, to measure time to first frame.</summary>
    public static readonly Stopwatch SinceStart = Stopwatch.StartNew();

    /// <summary>Repositories named on the command line; opened in addition to the restored tabs.</summary>
    public static IReadOnlyList<string> RepoArgs { get; private set; } = [];

    /// <summary>Render the window to this PNG once the repository is loaded, then exit (UI verification without desktop capture).</summary>
    public static string? SnapshotPath { get; } = Env("CHEESEGIT_SNAPSHOT");

    /// <summary>In snapshot mode, select this file (display path) so the diff shows.</summary>
    public static string? SnapshotFile { get; } = Env("CHEESEGIT_SNAPSHOT_FILE");

    /// <summary>In snapshot mode, switch to this theme before rendering.</summary>
    public static string? SnapshotTheme { get; } = Env("CHEESEGIT_SNAPSHOT_THEME");

    /// <summary>In snapshot mode, open the command log panel.</summary>
    public static bool SnapshotCommandLog { get; } = Env("CHEESEGIT_SNAPSHOT_LOG") == "1";

    /// <summary>In snapshot mode, show diffs side by side.</summary>
    public static bool SnapshotSplit { get; } = Env("CHEESEGIT_SNAPSHOT_SPLIT") == "1";

    /// <summary>In snapshot mode, open this dialog: settings, about, create, clone, confirm or branches (the branch dropdown).</summary>
    public static string? SnapshotDialog { get; } = Env("CHEESEGIT_SNAPSHOT_DIALOG");

    /// <summary>Opens the history tab; a row number also selects that commit (and <see cref="SnapshotFile"/> a file in it).</summary>
    public static string? SnapshotHistory { get; } = Env("CHEESEGIT_SNAPSHOT_HISTORY");

    /// <summary>Opens the Stash tab and selects this entry (`-1`: none); <see cref="SnapshotFile"/> then picks a file in it.</summary>
    public static string? SnapshotStash { get; } = Env("CHEESEGIT_SNAPSHOT_STASH");

    /// <summary>With <see cref="SnapshotHistory"/>, searches the history for this; with the branches dialog, types it into the branch search.</summary>
    public static string? SnapshotSearch { get; } = Env("CHEESEGIT_SNAPSHOT_SEARCH");

    private static SettingsStore? store;

    /// <summary>Loaded on first use, so tests can set an in-memory store before the app reads it.</summary>
    public static SettingsStore Store
    {
        // Snapshots must not restore the user's tabs or overwrite their settings.
        // An explicit CHEESEGIT_CONFIG_DIR is not the user's folder, so snapshots may keep settings there (the
        // update test carries What's new from one run to the next).
        get => store ??= SnapshotPath is null ? SettingsStore.Load(SettingsStore.DefaultDirectory, legacyDirectory: SettingsStore.DefaultLegacyDirectory)
            : Env("CHEESEGIT_CONFIG_DIR") is { } dir ? SettingsStore.Load(dir) : SettingsStore.InMemory();
        set => store = value;
    }

    /// <summary>Native Wayland is experimental in Avalonia 12.1, so it is opt-in (environment or settings).</summary>
    public static bool NativeWayland => Env("CHEESEGIT_WAYLAND") is { } v ? v == "1" : Store.Settings.NativeWayland;

    [STAThread]
    public static int Main(string[] args)
    {
        // git and ssh run this executable as their askpass helper: answer and exit before anything starts.
        if (CheeseGit.Core.Git.Askpass.TryRunHelper(args) is { } askpassExit) return askpassExit;
        // Must run first after that: Velopack's install, update and uninstall hooks start the app with special arguments and exit here.
        VelopackApp.Build().Run();
        RepoArgs = args.Where(a => a.Length > 0 && !a.StartsWith('-')).Select(Path.GetFullPath).ToList();
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Also used by the previewer.
    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
        // Launchers match windows to .desktop files by WM_CLASS (X11) and app id (Wayland), which default to the
        // assembly name. The packages' desktop files are named after the app id, so the windows carry it too.
        // Snapshots capture the window's own visual tree; popups (flyouts, menus)
        // are separate windows unless they live in its overlay layer.
        builder = builder.With(new X11PlatformOptions { WmClass = Services.DesktopEntry.AppId, OverlayPopups = SnapshotPath is not null });
        return NativeWayland ? builder.With(new WaylandPlatformOptions { AppId = Services.DesktopEntry.AppId }).UseWaylandWithFallback() : builder;
    }

    private static string? Env(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } v ? v : null;
}
