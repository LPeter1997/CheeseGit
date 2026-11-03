using System.Text.Json;
using System.Text.Json.Serialization;

namespace CheeseGit.App.Services;

/// <summary>What happens to local changes when switching branches (#52).</summary>
public enum SwitchChanges
{
    Ask,
    Bring,
    Leave,
}

public enum DiffViewMode
{
    Unified,
    Split,
}

/// <summary>Everything the app remembers between runs. Plain data; <see cref="SettingsStore"/> saves it.</summary>
public sealed class AppSettings
{
    public List<string> OpenRepositories { get; set; } = [];

    public int ActiveIndex { get; set; }

    /// <summary>Last folder used by the create and clone dialogs.</summary>
    public string? LastParentFolder { get; set; }

    /// <summary>A <see cref="Theming.ThemeManager.Choices"/> entry.</summary>
    public string Theme { get; set; } = "System";

    public DiffViewMode DiffViewMode { get; set; } = DiffViewMode.Unified;

    /// <summary>Unset until the window has been closed once: it then opens at its default size, centred.</summary>
    public WindowPlacement? Window { get; set; }

    public double LeftPanelWidth { get; set; } = 340;

    public double CommitFormHeight { get; set; } = 170;

    public bool CommandLogOpen { get; set; }

    public double CommandLogHeight { get; set; } = 240;

    /// <summary>Repositories already offered a commit-graph file: the offer is made once, never repeated.</summary>
    public List<string> CommitGraphOffered { get; set; } = [];

    public SwitchChanges SwitchChanges { get; set; } = SwitchChanges.Ask;

    /// <summary>The merge tool chosen in the conflict dialog (a <c>MergeTool.Id</c>); the first installed one when unset or gone.</summary>
    public string? PreferredMergeTool { get; set; }

    /// <summary>Avalonia's native Wayland backend is experimental, so it is opt-in. Takes effect on restart.</summary>
    public bool NativeWayland { get; set; }

    /// <summary>The AppImage's "add to the applications menu" prompt was closed: the options menu still offers it.</summary>
    public bool DesktopEntryDismissed { get; set; }

    /// <summary>
    /// Git LFS images up to this size are downloaded when their diff is shown, if they are not in the
    /// local store (old versions usually are not: git-lfs fetches what a checkout needs). 0: only on request.
    /// </summary>
    public int LfsAutoDownloadMegabytes { get; set; } = 10;

    /// <summary>Look for a new version when the app starts (installed copies only).</summary>
    public bool CheckForUpdates { get; set; } = true;

    /// <summary>A version the user chose to skip: not offered on start again (a manual check still shows it).</summary>
    public string? SkippedVersion { get; set; }

    /// <summary>Release notes of the update being installed, shown once as "What's new" by that version.</summary>
    public string? PendingChangelog { get; set; }

    public string? PendingChangelogVersion { get; set; }
}

[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;

/// <summary>The old Tauri app's <c>state.json</c> (<c>src-tauri/src/state/mod.rs</c>), read once to carry its settings over.</summary>
internal sealed record LegacyAppState(
    [property: JsonPropertyName("open_repos")] List<string>? OpenRepos,
    [property: JsonPropertyName("active_index")] int? ActiveIndex,
    [property: JsonPropertyName("last_parent_folder")] string? LastParentFolder,
    [property: JsonPropertyName("theme")] string? Theme,
    [property: JsonPropertyName("preferred_merge_tool")] string? PreferredMergeTool)
{
    /// <summary>
    /// Tauri's <c>app_config_dir()</c> for <c>com.cheesegit.desktop</c>. .NET's ApplicationData is
    /// <c>~/.config</c> on macOS too, so the Library folder is spelled out there.
    /// </summary>
    public static string DefaultDirectory => OperatingSystem.IsMacOS()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "com.cheesegit.desktop")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "com.cheesegit.desktop");

    /// <summary>
    /// The old app's open tabs, theme, last folder and merge tool. Its skipped version is left behind (a version
    /// of the old app), and so are tabs whose folder is gone, which would only open as errors.
    /// </summary>
    public static AppSettings? TryMigrate(string directory)
    {
        LegacyAppState? old;
        try
        {
            var file = Path.Combine(directory, "state.json");
            if (!File.Exists(file)) return null;
            old = JsonSerializer.Deserialize(File.ReadAllText(file), LegacyJsonContext.Default.LegacyAppState);
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
        if (old is null) return null;
        var settings = new AppSettings();
        var repos = old.OpenRepos ?? [];
        var active = old.ActiveIndex is { } i && i >= 0 && i < repos.Count ? repos[i] : null;
        settings.OpenRepositories = repos.Where(Directory.Exists).ToList();
        settings.ActiveIndex = active is null ? 0 : Math.Max(0, settings.OpenRepositories.IndexOf(active));
        settings.LastParentFolder = old.LastParentFolder is { } folder && Directory.Exists(folder) ? folder : null;
        settings.Theme = old.Theme switch
        {
            "light" => "Light",
            "dark" => "Dark",
            "high-contrast" => "High contrast",
            _ => "System",
        };
        settings.PreferredMergeTool = old.PreferredMergeTool;
        return settings;
    }
}

[JsonSerializable(typeof(LegacyAppState))]
internal sealed partial class LegacyJsonContext : JsonSerializerContext;

/// <summary>
/// Loads and saves <see cref="AppSettings"/> as JSON in the user's config
/// folder. Without a settings file, the old app's <c>state.json</c> is carried
/// over once (<see cref="LegacyAppState"/>). <c>CHEESEGIT_CONFIG_DIR</c>
/// overrides the folder (and skips that), and a read-only store (snapshot runs,
/// tests) never writes.
/// </summary>
public sealed class SettingsStore
{
    private readonly string? path;

    private SettingsStore(string? path, AppSettings settings)
    {
        this.path = path;
        this.Settings = settings;
    }

    public AppSettings Settings { get; }

    public static string DefaultDirectory =>
        Environment.GetEnvironmentVariable("CHEESEGIT_CONFIG_DIR") is { Length: > 0 } dir
            ? dir
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CheeseGit");

    /// <summary>The old app's folder to migrate from; none when <c>CHEESEGIT_CONFIG_DIR</c> points elsewhere.</summary>
    public static string? DefaultLegacyDirectory =>
        Environment.GetEnvironmentVariable("CHEESEGIT_CONFIG_DIR") is { Length: > 0 } ? null : LegacyAppState.DefaultDirectory;

    public static SettingsStore Load(string directory, bool readOnly = false, string? legacyDirectory = null)
    {
        var file = Path.Combine(directory, "settings.json");
        AppSettings? settings = null;
        var exists = File.Exists(file);
        try
        {
            if (exists) settings = JsonSerializer.Deserialize(File.ReadAllText(file), SettingsJsonContext.Default.AppSettings);
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            // A corrupt or unreadable file must not stop the app from starting.
        }
        var store = new SettingsStore(readOnly ? null : file, settings ?? (!exists && legacyDirectory is not null ? LegacyAppState.TryMigrate(legacyDirectory) : null) ?? new AppSettings());
        // Written at once, so the migration runs a single time even if the app closes before changing anything.
        if (!exists && settings is null) store.Save();
        return store;
    }

    /// <summary>In-memory settings that are never written (snapshot runs and UI tests).</summary>
    public static SettingsStore InMemory(AppSettings? settings = null) => new(null, settings ?? new AppSettings());

    public void Save()
    {
        if (this.path is null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(this.path)!);
            // Write then rename, so a crash mid-write cannot leave a truncated file.
            var temp = this.path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(this.Settings, SettingsJsonContext.Default.AppSettings));
            File.Move(temp, this.path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Losing a setting is better than crashing.
        }
    }
}
