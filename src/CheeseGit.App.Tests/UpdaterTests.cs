using Avalonia;
using Avalonia.Headless.XUnit;
using CheeseGit.App.Controls;
using CheeseGit.App.Services;
using CheeseGit.App.ViewModels;
using CheeseGit.Core.Git;

namespace CheeseGit.App.Tests;

/// <summary>The update banner's flow (the old app's updater), against a fake release source.</summary>
public sealed class UpdaterTests
{
    private sealed class FakeUpdates(UpdateMode mode = UpdateMode.InApp) : IUpdateService
    {
        public UpdateMode Mode => mode;

        public AvailableUpdate? Latest { get; set; } = new("1.2.0", "- New things", "https://example.invalid/1.2.0");

        public Exception? CheckFails { get; set; }

        public Exception? DownloadFails { get; set; }

        public List<string> Calls { get; } = [];

        public Task<AvailableUpdate?> CheckAsync(CancellationToken ct = default)
        {
            this.Calls.Add("check");
            return this.CheckFails is { } e ? Task.FromException<AvailableUpdate?>(e) : Task.FromResult(this.Latest);
        }

        public Task DownloadAsync(AvailableUpdate update, Action<int> progress, CancellationToken ct = default)
        {
            this.Calls.Add("download " + update.Version);
            if (this.DownloadFails is { } e) return Task.FromException(e);
            progress(50);
            progress(100);
            return Task.CompletedTask;
        }

        public void ApplyAndRestart(AvailableUpdate update) => this.Calls.Add("restart " + update.Version);

        public void ApplyOnExit(AvailableUpdate update) => this.Calls.Add("on-exit " + update.Version);
    }

    private static MainViewModel CreateMain(IUpdateService updates, AppSettings? settings = null) =>
        new(SettingsStore.InMemory(settings), ((App)Application.Current!).Themes, new GitRunner(), "1.1.0", updates);

    [AvaloniaFact]
    public async Task StartupCheckOffersANewerVersion()
    {
        var fake = new FakeUpdates();
        using var main = CreateMain(fake);
        await main.Updater.CheckOnStartAsync();
        Assert.True(main.Updater.ShowBanner);
        Assert.Equal("Version 1.2.0 is available", main.Updater.BannerText);
        Assert.True(main.Updater.IsInApp);
    }

    [AvaloniaFact]
    public async Task StartupCheckRespectsTheSettingAndTheSkippedVersion()
    {
        var off = new FakeUpdates();
        using (var main = CreateMain(off, new AppSettings { CheckForUpdates = false }))
        {
            await main.Updater.CheckOnStartAsync();
            Assert.Empty(off.Calls);
        }

        var skipped = new FakeUpdates();
        using (var main = CreateMain(skipped, new AppSettings { SkippedVersion = "1.2.0" }))
        {
            await main.Updater.CheckOnStartAsync();
            Assert.False(main.Updater.ShowBanner);
            // A manual check still offers it: the user asked.
            await main.Updater.CheckNowCommand.ExecuteAsync(null);
            Assert.True(main.Updater.ShowBanner);
        }
    }

    [AvaloniaFact]
    public async Task StartupFailureIsSilentButAManualOneIsReported()
    {
        var fake = new FakeUpdates { CheckFails = new HttpRequestException("offline") };
        using var main = CreateMain(fake);
        await main.Updater.CheckOnStartAsync();
        Assert.Empty(main.Alerts);
        await main.Updater.CheckNowCommand.ExecuteAsync(null);
        var alert = Assert.Single(main.Alerts);
        Assert.True(alert.IsError);
        Assert.Contains("offline", alert.Message);
    }

    [AvaloniaFact]
    public async Task ManualCheckSaysUpToDate()
    {
        using var main = CreateMain(new FakeUpdates { Latest = null });
        await main.Updater.CheckNowCommand.ExecuteAsync(null);
        Assert.False(main.Updater.ShowBanner);
        Assert.Equal("CheeseGit is up to date.", Assert.Single(main.Alerts).Message);
    }

    [AvaloniaFact]
    public async Task UpdateNowDownloadsKeepsTheNotesAndRestarts()
    {
        var fake = new FakeUpdates();
        using var main = CreateMain(fake);
        await main.Updater.CheckNowCommand.ExecuteAsync(null);
        await main.Updater.UpdateNowCommand.ExecuteAsync(null);
        Assert.Equal(["check", "download 1.2.0", "restart 1.2.0"], fake.Calls);
        Assert.Equal("1.2.0", main.Settings.PendingChangelogVersion);
        Assert.Equal("- New things", main.Settings.PendingChangelog);
    }

    [AvaloniaFact]
    public async Task OnExitHandsTheUpdateToVelopackAndHidesTheBanner()
    {
        var fake = new FakeUpdates();
        using var main = CreateMain(fake);
        await main.Updater.CheckNowCommand.ExecuteAsync(null);
        await main.Updater.UpdateOnExitCommand.ExecuteAsync(null);
        Assert.Equal(["check", "download 1.2.0", "on-exit 1.2.0"], fake.Calls);
        Assert.False(main.Updater.ShowBanner);
        Assert.True(main.Updater.ReadyOnExit);
        Assert.Contains("when you close", Assert.Single(main.Alerts).Message);
        // Checking again does not offer what is already waiting.
        await main.Updater.CheckNowCommand.ExecuteAsync(null);
        Assert.False(main.Updater.ShowBanner);
    }

    [AvaloniaFact]
    public async Task AFailedDownloadKeepsTheBannerAndReports()
    {
        var fake = new FakeUpdates { DownloadFails = new IOException("disk full") };
        using var main = CreateMain(fake);
        await main.Updater.CheckNowCommand.ExecuteAsync(null);
        await main.Updater.UpdateNowCommand.ExecuteAsync(null);
        Assert.DoesNotContain(fake.Calls, c => c.StartsWith("restart"));
        Assert.True(main.Updater.ShowBanner);
        Assert.False(main.Updater.IsDownloading);
        Assert.Contains("disk full", Assert.Single(main.Alerts).Message);
        Assert.Null(main.Settings.PendingChangelogVersion);
    }

    [AvaloniaFact]
    public async Task SkipRemembersTheVersionAndDismissDoesNot()
    {
        using var main = CreateMain(new FakeUpdates());
        await main.Updater.CheckNowCommand.ExecuteAsync(null);
        main.Updater.DismissCommand.Execute(null);
        Assert.False(main.Updater.ShowBanner);
        Assert.Null(main.Settings.SkippedVersion);

        await main.Updater.CheckNowCommand.ExecuteAsync(null);
        main.Updater.SkipCommand.Execute(null);
        Assert.False(main.Updater.ShowBanner);
        Assert.Equal("1.2.0", main.Settings.SkippedVersion);
    }

    [AvaloniaFact]
    public async Task NoticeOnlyBuildsNeverDownload()
    {
        var fake = new FakeUpdates(UpdateMode.NoticeOnly);
        using var main = CreateMain(fake);
        await main.Updater.CheckOnStartAsync();
        Assert.True(main.Updater.IsNoticeOnly);
        Assert.False(main.Updater.IsInApp);
        await main.Updater.UpdateNowCommand.ExecuteAsync(null);
        Assert.Equal(["check"], fake.Calls);
    }

    [AvaloniaFact]
    public async Task UninstalledCopiesNeverCheck()
    {
        var fake = new FakeUpdates(UpdateMode.None);
        using var main = CreateMain(fake);
        await main.Updater.CheckOnStartAsync();
        Assert.False(main.Updater.CanCheck);
        Assert.False(main.Updater.CheckNowCommand.CanExecute(null));
        Assert.Empty(fake.Calls);
    }

    [AvaloniaFact]
    public void WhatsNewShowsOnceForTheInstalledVersion()
    {
        using var main = CreateMain(new FakeUpdates(), new AppSettings { PendingChangelog = "- Fixes", PendingChangelogVersion = "1.1.0" });
        main.Updater.ShowWhatsNewIfUpdated("1.1.0");
        var dialog = Assert.IsType<WhatsNewDialogViewModel>(main.Dialog);
        Assert.Equal("What's new in 1.1.0", dialog.Title);
        Assert.Null(main.Settings.PendingChangelogVersion);
    }

    [AvaloniaFact]
    public void WhatsNewWaitsForAnUpdateOnExitAndDropsStaleNotes()
    {
        using (var main = CreateMain(new FakeUpdates(), new AppSettings { PendingChangelog = "- Later", PendingChangelogVersion = "1.2.0" }))
        {
            main.Updater.ShowWhatsNewIfUpdated("1.1.0");
            Assert.Null(main.Dialog);
            Assert.Equal("1.2.0", main.Settings.PendingChangelogVersion);
        }
        using (var main = CreateMain(new FakeUpdates(), new AppSettings { PendingChangelog = "- Old", PendingChangelogVersion = "1.0.0" }))
        {
            main.Updater.ShowWhatsNewIfUpdated("1.1.0");
            Assert.Null(main.Dialog);
            Assert.Null(main.Settings.PendingChangelogVersion);
        }
    }

    [Theory]
    [InlineData("1.2.0", "1.1.9", 1)]
    [InlineData("v1.2.0", "1.2.0", 0)]
    [InlineData("1.10.0", "1.9.0", 1)]
    [InlineData("1.2.0-beta.1", "1.2.0", -1)]
    [InlineData("1.2.0-beta.2", "1.2.0-beta.1", 1)]
    [InlineData("1.2", "1.2.0", 0)]
    [InlineData("2", "1.9.9", 1)]
    [InlineData("1.2.0+abc", "1.2.0", 0)]
    [InlineData("nightly", "1.0.0", -1)]
    [InlineData("1.0.0", "dev build", 1)]
    public void CompareVersions(string a, string b, int expected) =>
        Assert.Equal(expected, Math.Sign(ReleaseSource.CompareVersions(a, b)));

    [Fact]
    public void MarkdownBlocks()
    {
        var blocks = MarkdownView.Parse("""
            ## Highlights

            - First item
              continued here
            - Second
              - Nested
            1. Numbered

            A paragraph
            on two lines.
            ```
            code *not* parsed
            ```
            """);
        Assert.Equal(
            [
                new(MarkdownView.BlockKind.Heading, "Highlights", 2),
                new(MarkdownView.BlockKind.Bullet, "First item continued here", 0),
                new(MarkdownView.BlockKind.Bullet, "Second", 0),
                new(MarkdownView.BlockKind.Bullet, "Nested", 1),
                new(MarkdownView.BlockKind.Numbered, "Numbered", 0, "1."),
                new(MarkdownView.BlockKind.Paragraph, "A paragraph on two lines."),
                new MarkdownView.Block(MarkdownView.BlockKind.Code, "code *not* parsed"),
            ],
            blocks);
    }

    [Fact]
    public void MarkdownInline()
    {
        var spans = MarkdownView.ParseInline("Use **bold**, *it*, `a*b` and [the page](https://x.invalid); snake_case and a * b stay.");
        Assert.Equal(
            [
                new("Use "),
                new("bold", Bold: true),
                new(", "),
                new("it", Italic: true),
                new(", "),
                new("a*b", Code: true),
                new(" and "),
                new("the page", Link: true),
                new("; snake_case and a * b stay."),
            ],
            spans);
    }
}
