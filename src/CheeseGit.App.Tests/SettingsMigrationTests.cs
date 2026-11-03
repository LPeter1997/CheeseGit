using CheeseGit.App.Services;

namespace CheeseGit.App.Tests;

/// <summary>The old app's <c>state.json</c> carries over once, into a new settings file (Phase 6).</summary>
public sealed class SettingsMigrationTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("cheesegit-settings-").FullName;

    private string Config => Path.Combine(this.root, "CheeseGit");

    private string Legacy => Path.Combine(this.root, "com.cheesegit.desktop");

    private string Folder(string name) => Directory.CreateDirectory(Path.Combine(this.root, name)).FullName;

    private void WriteLegacy(string json)
    {
        Directory.CreateDirectory(this.Legacy);
        File.WriteAllText(Path.Combine(this.Legacy, "state.json"), json);
    }

    private static string Json(string s) => System.Text.Json.JsonSerializer.Serialize(s);

    [Fact]
    public void TabsThemeFolderAndMergeToolCarryOver()
    {
        var (a, b) = (this.Folder("a"), this.Folder("b"));
        var gone = Path.Combine(this.root, "gone");
        this.WriteLegacy($$"""
            {
              "open_repos": [{{Json(a)}}, {{Json(gone)}}, {{Json(b)}}],
              "active_index": 2,
              "last_parent_folder": {{Json(this.root)}},
              "skipped_version": "0.9.0",
              "pending_changelog": "old notes",
              "dismiss_desktop_entry": true,
              "theme": "high-contrast",
              "preferred_merge_tool": "kdiff3"
            }
            """);
        var store = SettingsStore.Load(this.Config, legacyDirectory: this.Legacy);
        var s = store.Settings;
        Assert.Equal([a, b], s.OpenRepositories);
        Assert.Equal(1, s.ActiveIndex); // Still b, after the missing folder was dropped.
        Assert.Equal(this.root, s.LastParentFolder);
        Assert.Equal("High contrast", s.Theme);
        Assert.Equal("kdiff3", s.PreferredMergeTool);
        Assert.Null(s.SkippedVersion);
        Assert.Null(s.PendingChangelog);
        Assert.True(File.Exists(Path.Combine(this.Config, "settings.json")));
    }

    [Fact]
    public void MigratesOnlyOnce()
    {
        var a = this.Folder("a");
        this.WriteLegacy($$"""{ "open_repos": [{{Json(a)}}], "theme": "light" }""");
        var first = SettingsStore.Load(this.Config, legacyDirectory: this.Legacy);
        Assert.Equal("Light", first.Settings.Theme);
        first.Settings.Theme = "Dark";
        first.Settings.OpenRepositories.Clear();
        first.Save();

        var second = SettingsStore.Load(this.Config, legacyDirectory: this.Legacy);
        Assert.Equal("Dark", second.Settings.Theme);
        Assert.Empty(second.Settings.OpenRepositories);
    }

    [Fact]
    public void ExistingSettingsWin()
    {
        Directory.CreateDirectory(this.Config);
        File.WriteAllText(Path.Combine(this.Config, "settings.json"), """{ "Theme": "Dark" }""");
        this.WriteLegacy("""{ "theme": "light" }""");
        Assert.Equal("Dark", SettingsStore.Load(this.Config, legacyDirectory: this.Legacy).Settings.Theme);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("""{ "open_repos": 5 }""")]
    [InlineData("null")]
    public void ABrokenStateFileGivesDefaults(string json)
    {
        this.WriteLegacy(json);
        var s = SettingsStore.Load(this.Config, legacyDirectory: this.Legacy).Settings;
        Assert.Empty(s.OpenRepositories);
        Assert.Equal("System", s.Theme);
    }

    [Theory]
    [InlineData("system", "System")]
    [InlineData("dark", "Dark")]
    [InlineData("sepia", "System")]
    public void ThemeNames(string old, string expected)
    {
        this.WriteLegacy($$"""{ "theme": {{Json(old)}}, "active_index": -1 }""");
        Assert.Equal(expected, SettingsStore.Load(this.Config, legacyDirectory: this.Legacy).Settings.Theme);
    }

    [Fact]
    public void NoLegacyFolderGivesDefaults()
    {
        var s = SettingsStore.Load(this.Config, legacyDirectory: this.Legacy).Settings;
        Assert.Empty(s.OpenRepositories);
        Assert.Equal("System", s.Theme);
    }

    public void Dispose() => Directory.Delete(this.root, recursive: true);
}
