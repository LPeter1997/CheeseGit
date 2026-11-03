using CheeseGit.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CheeseGit.App.ViewModels;

/// <summary>
/// The update banner and "Check for Updates" (the old app's updater feature): Update now, On exit, Skip,
/// and "What's new" once the new version runs. Distro builds only get a notice with a link.
/// </summary>
public sealed partial class UpdaterViewModel(MainViewModel owner, IUpdateService service) : ObservableObject
{
    public UpdateMode Mode => service.Mode;

    public bool CanCheck => service.Mode != UpdateMode.None;

    public bool IsInApp => service.Mode == UpdateMode.InApp;

    public bool IsNoticeOnly => service.Mode == UpdateMode.NoticeOnly;

    /// <summary>The offered update; the banner shows while this is set.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowBanner), nameof(BannerText))]
    public partial AvailableUpdate? Available { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BannerText), nameof(CanAct))]
    public partial bool IsDownloading { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BannerText))]
    public partial int Progress { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckNowCommand))]
    public partial bool IsChecking { get; private set; }

    /// <summary>Downloaded and handed to Velopack, which installs it once the app exits.</summary>
    [ObservableProperty]
    public partial bool ReadyOnExit { get; private set; }

    public bool ShowBanner => this.Available is not null;

    public bool CanAct => !this.IsDownloading;

    public string BannerText => this.Available is not { } update ? ""
        : this.IsDownloading ? $"Downloading version {update.Version} ({this.Progress}%)…"
        : $"Version {update.Version} is available";

    /// <summary>On start: silent when offline, and a skipped version stays skipped.</summary>
    public async Task CheckOnStartAsync()
    {
        if (service.Mode == UpdateMode.None || !owner.Settings.CheckForUpdates) return;
        try
        {
            if (await service.CheckAsync() is { } update && update.Version != owner.Settings.SkippedVersion) this.Offer(update);
        }
        catch (Exception)
        {
            // No network, a proxy or GitHub's rate limit: not worth a red banner on every start.
        }
    }

    [RelayCommand(CanExecute = nameof(CanCheckNow))]
    private async Task CheckNow()
    {
        this.IsChecking = true;
        try
        {
            if (await service.CheckAsync() is { } update) this.Offer(update);
            else owner.ShowInfo("CheeseGit is up to date.");
        }
        catch (Exception e)
        {
            owner.ShowError($"Could not check for updates: {e.Message}");
        }
        finally
        {
            this.IsChecking = false;
        }
    }

    private bool CanCheckNow() => !this.IsChecking && service.Mode != UpdateMode.None;

    private void Offer(AvailableUpdate update)
    {
        if (this.ReadyOnExit || this.IsDownloading) return;
        this.Available = update;
    }

    [RelayCommand]
    private async Task UpdateNow()
    {
        if (!await this.DownloadAsync() || this.Available is not { } update) return;
        try
        {
            service.ApplyAndRestart(update);
        }
        catch (Exception e)
        {
            owner.ShowError($"Could not install the update: {e.Message}");
        }
    }

    [RelayCommand]
    private async Task UpdateOnExit()
    {
        if (!await this.DownloadAsync() || this.Available is not { } update) return;
        try
        {
            service.ApplyOnExit(update);
            this.ReadyOnExit = true;
            this.Available = null;
            owner.ShowInfo($"Version {update.Version} will be installed when you close CheeseGit.");
        }
        catch (Exception e)
        {
            owner.ShowError($"Could not install the update: {e.Message}");
        }
    }

    private async Task<bool> DownloadAsync()
    {
        if (this.Available is not { } update || this.IsDownloading || service.Mode != UpdateMode.InApp) return false;
        this.IsDownloading = true;
        this.Progress = 0;
        try
        {
            // Velopack reports progress from its own threads.
            await service.DownloadAsync(update, p => Avalonia.Threading.Dispatcher.UIThread.Post(() => this.Progress = Math.Clamp(p, 0, 100)));
            this.Progress = 100;
            // The new version shows these once it runs (What's new).
            owner.Settings.PendingChangelog = update.NotesMarkdown;
            owner.Settings.PendingChangelogVersion = update.Version;
            owner.Store.Save();
            return true;
        }
        catch (Exception e)
        {
            owner.ShowError($"Could not download the update: {e.Message}");
            return false;
        }
        finally
        {
            this.IsDownloading = false;
        }
    }

    [RelayCommand]
    private void Skip()
    {
        if (this.Available is not { } update) return;
        owner.Settings.SkippedVersion = update.Version;
        owner.Store.Save();
        this.Available = null;
    }

    /// <summary>Remind me later: the next start offers it again.</summary>
    [RelayCommand]
    private void Dismiss() => this.Available = null;

    [RelayCommand]
    private Task OpenReleasePage() =>
        this.Available?.ReleaseUrl is { } url ? owner.OpenUrlAsync(url) : owner.OpenUrlAsync(ReleaseSource.RepositoryUrl + "/releases/latest");

    /// <summary>The first start of a new version shows its notes once; stale notes (an update that never applied) are dropped.</summary>
    public void ShowWhatsNewIfUpdated(string currentVersion)
    {
        var settings = owner.Settings;
        if (settings.PendingChangelogVersion is not { } version) return;
        var order = ReleaseSource.CompareVersions(version, currentVersion);
        if (order > 0) return; // Still waiting for "On exit" to install it.
        var notes = settings.PendingChangelog;
        settings.PendingChangelog = null;
        settings.PendingChangelogVersion = null;
        owner.Store.Save();
        if (order == 0 && !string.IsNullOrWhiteSpace(notes)) owner.ShowWhatsNew(version, notes);
    }
}

/// <summary>Release notes of the version that just got installed.</summary>
public sealed partial class WhatsNewDialogViewModel(string version, string notesMarkdown) : DialogViewModel
{
    public string Title { get; } = $"What's new in {version}";

    public string Notes { get; } = notesMarkdown;
}
