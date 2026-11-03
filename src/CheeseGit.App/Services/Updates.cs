using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CheeseGit.App.Services;

/// <summary>How this copy of the app gets new versions (docs/release.md).</summary>
public enum UpdateMode
{
    /// <summary>Not installed by Velopack (a dev build, or run from the publish folder): no checks.</summary>
    None,

    /// <summary>Installed by Velopack (Setup.exe, portable zip, AppImage, macOS package): download and apply in the app.</summary>
    InApp,

    /// <summary>Distro packages, Flatpak and AUR are updated by the system: only say that a new version exists.</summary>
    NoticeOnly,
}

/// <summary>A newer release. <see cref="Handle"/> is the source's own object, passed back to download and apply.</summary>
public sealed record AvailableUpdate(string Version, string? NotesMarkdown, string? ReleaseUrl, object? Handle = null);

/// <summary>Checks for, downloads and applies updates. Tests substitute a fake.</summary>
public interface IUpdateService
{
    UpdateMode Mode { get; }

    /// <summary>Null when the app is up to date. Throws when the check fails (offline, rate limited).</summary>
    Task<AvailableUpdate?> CheckAsync(CancellationToken ct = default);

    /// <summary><see cref="UpdateMode.InApp"/> only. <paramref name="progress"/> gets 0–100.</summary>
    Task DownloadAsync(AvailableUpdate update, Action<int> progress, CancellationToken ct = default);

    /// <summary>Applies a downloaded update and restarts the app. Does not return when it works.</summary>
    void ApplyAndRestart(AvailableUpdate update);

    /// <summary>Applies a downloaded update once the app has exited, without starting it again.</summary>
    void ApplyOnExit(AvailableUpdate update);
}

/// <summary>Where releases are published: GitHub, like the old app's updater (decided 2026-11-02).</summary>
public static class ReleaseSource
{
    public const string RepositoryUrl = "https://github.com/LPeter1997/CheeseGit";

    public const string LatestReleaseApi = "https://api.github.com/repos/LPeter1997/CheeseGit/releases/latest";

    /// <summary>
    /// <c>CHEESEGIT_UPDATE_SOURCE</c>: a folder or URL with vpk's output (releases.&lt;channel&gt;.json and the
    /// packages) instead of the GitHub releases, to try an update before publishing it.
    /// </summary>
    public static string? Override =>
        Environment.GetEnvironmentVariable("CHEESEGIT_UPDATE_SOURCE") is { Length: > 0 } source ? source : null;

    /// <summary>
    /// The project file sets <c>NO_UPDATER</c> for distro builds (<c>-p:CheeseGitUpdater=false</c>), so the
    /// Velopack updater cannot run where the system owns the files.
    /// </summary>
    public static IUpdateService Create(string currentVersion)
    {
#if NO_UPDATER
        return new ReleaseNoticeService(currentVersion);
#else
        var velopack = new VelopackUpdateService(Override);
        return velopack.Mode == UpdateMode.InApp ? velopack : new NoUpdateService();
#endif
    }

    /// <summary>
    /// Compares release versions ("1.2.3", "v1.2.3", "1.2.3-beta.1"). Pre-release suffixes sort before the
    /// release; anything unparsable compares as older, so a malformed tag never offers itself.
    /// </summary>
    public static int CompareVersions(string a, string b)
    {
        static (Version, string?)? Parse(string s)
        {
            s = s.Trim().TrimStart('v', 'V');
            var dash = s.IndexOf('-');
            var core = dash < 0 ? s : s[..dash];
            var pre = dash < 0 ? null : s[(dash + 1)..];
            var plus = core.IndexOf('+');
            if (plus >= 0) core = core[..plus];
            if (!Version.TryParse(core.Contains('.') ? core : core + ".0", out var v)) return null;
            return (new Version(v.Major, v.Minor, Math.Max(v.Build, 0)), pre);
        }
        var (pa, pb) = (Parse(a), Parse(b));
        if (pa is null || pb is null) return pa is null ? (pb is null ? 0 : -1) : 1;
        var byNumber = pa.Value.Item1.CompareTo(pb.Value.Item1);
        if (byNumber != 0) return byNumber;
        return (pa.Value.Item2, pb.Value.Item2) switch
        {
            (null, null) => 0,
            (null, _) => 1,
            (_, null) => -1,
            var (x, y) => string.CompareOrdinal(x, y),
        };
    }
}

/// <summary>A copy that cannot update itself: dev builds and runs from a publish folder.</summary>
public sealed class NoUpdateService : IUpdateService
{
    public UpdateMode Mode => UpdateMode.None;

    public Task<AvailableUpdate?> CheckAsync(CancellationToken ct = default) => Task.FromResult<AvailableUpdate?>(null);

    public Task DownloadAsync(AvailableUpdate update, Action<int> progress, CancellationToken ct = default) => throw new NotSupportedException();

    public void ApplyAndRestart(AvailableUpdate update) => throw new NotSupportedException();

    public void ApplyOnExit(AvailableUpdate update) => throw new NotSupportedException();
}

#if !NO_UPDATER
/// <summary>Velopack against the GitHub releases: delta packages, applied by Velopack's updater process.</summary>
public sealed class VelopackUpdateService(string? source = null) : IUpdateService
{
    // A path or URL string becomes Velopack's file or web source.
    private readonly Velopack.UpdateManager manager = source is null
        ? new(new Velopack.Sources.GithubSource(ReleaseSource.RepositoryUrl, null, false))
        : new(source);

    public UpdateMode Mode => this.manager.IsInstalled ? UpdateMode.InApp : UpdateMode.None;

    public async Task<AvailableUpdate?> CheckAsync(CancellationToken ct = default)
    {
        if (!this.manager.IsInstalled) return null;
        var info = await this.manager.CheckForUpdatesAsync().WaitAsync(ct);
        if (info is null) return null;
        var target = info.TargetFullRelease;
        var page = source is null ? $"{ReleaseSource.RepositoryUrl}/releases/tag/v{target.Version}" : null;
        return new AvailableUpdate(target.Version.ToString(), target.NotesMarkdown, page, info);
    }

    public Task DownloadAsync(AvailableUpdate update, Action<int> progress, CancellationToken ct = default) =>
        this.manager.DownloadUpdatesAsync((Velopack.UpdateInfo)update.Handle!, progress, ct);

    public void ApplyAndRestart(AvailableUpdate update) => this.manager.ApplyUpdatesAndRestart(((Velopack.UpdateInfo)update.Handle!).TargetFullRelease);

    public void ApplyOnExit(AvailableUpdate update) =>
        this.manager.WaitExitThenApplyUpdates(((Velopack.UpdateInfo)update.Handle!).TargetFullRelease, silent: true, restart: false);
}
#endif

/// <summary>Asks GitHub for the latest release and compares versions; never downloads anything.</summary>
public sealed class ReleaseNoticeService(string currentVersion) : IUpdateService
{
    private static readonly HttpClient Http = CreateClient();

    public UpdateMode Mode => UpdateMode.NoticeOnly;

    public async Task<AvailableUpdate?> CheckAsync(CancellationToken ct = default)
    {
        using var stream = await Http.GetStreamAsync(ReleaseSource.LatestReleaseApi, ct);
        var release = await JsonSerializer.DeserializeAsync(stream, ReleaseJsonContext.Default.GithubRelease, ct);
        if (release?.TagName is not { Length: > 0 } tag || release.Draft || release.Prerelease) return null;
        if (ReleaseSource.CompareVersions(tag, currentVersion) <= 0) return null;
        return new AvailableUpdate(tag.TrimStart('v', 'V'), release.Body, release.HtmlUrl);
    }

    public Task DownloadAsync(AvailableUpdate update, Action<int> progress, CancellationToken ct = default) => throw new NotSupportedException();

    public void ApplyAndRestart(AvailableUpdate update) => throw new NotSupportedException();

    public void ApplyOnExit(AvailableUpdate update) => throw new NotSupportedException();

    private static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        // GitHub's API rejects requests without a User-Agent.
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("CheeseGit", "1"));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return http;
    }
}

internal sealed record GithubRelease(
    [property: JsonPropertyName("tag_name")] string? TagName,
    [property: JsonPropertyName("body")] string? Body,
    [property: JsonPropertyName("html_url")] string? HtmlUrl,
    [property: JsonPropertyName("draft")] bool Draft,
    [property: JsonPropertyName("prerelease")] bool Prerelease);

[JsonSerializable(typeof(GithubRelease))]
internal sealed partial class ReleaseJsonContext : JsonSerializerContext;

/// <summary>Snapshot runs (<c>CHEESEGIT_SNAPSHOT_DIALOG=update|update-notice</c>): a canned update, no network.</summary>
internal sealed class SnapshotUpdateService(UpdateMode mode) : IUpdateService
{
    public UpdateMode Mode => mode;

    public Task<AvailableUpdate?> CheckAsync(CancellationToken ct = default) =>
        Task.FromResult<AvailableUpdate?>(new AvailableUpdate("1.1.0", "- Updates", ReleaseSource.RepositoryUrl + "/releases"));

    public Task DownloadAsync(AvailableUpdate update, Action<int> progress, CancellationToken ct = default) => Task.CompletedTask;

    public void ApplyAndRestart(AvailableUpdate update) { }

    public void ApplyOnExit(AvailableUpdate update) { }
}
