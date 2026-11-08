using Avalonia.Threading;
using System.Collections.ObjectModel;
using CheeseGit.App.Services;
using CheeseGit.App.Theming;
using CheeseGit.Core.Git;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CheeseGit.App.ViewModels;

/// <summary>Platform services the window provides (pickers, clipboard). Tests substitute them.</summary>
public interface IPlatformServices
{
    Task<string?> PickFolderAsync(string title);

    Task SetClipboardTextAsync(string text);

    Task SaveTextFileAsync(string title, string suggestedName, string text);

    /// <summary>Asks where to save and writes <paramref name="content"/> there; false if the user cancelled.</summary>
    Task<bool> SaveFileAsync(string title, string suggestedName, byte[] content);

    /// <summary>The window as a PNG, anonymized when <paramref name="anonymizer"/> is given (problem reports).</summary>
    Task<byte[]?> CaptureWindowAsync(Core.Diagnostics.Anonymizer? anonymizer);

    Task OpenUrlAsync(string url);
}

/// <summary>
/// The window: repository tabs, the welcome screen, alerts, dialogs and the
/// command log. Each tab owns its own <see cref="Core.Repository.RepositorySession"/>.
/// </summary>
public sealed partial class MainViewModel : ObservableObject, IAppShell, IDisposable
{
    private readonly SettingsStore store;
    private bool restoring;

    public MainViewModel(SettingsStore store, ThemeManager themes, GitRunner git, string appVersion, IUpdateService? updates = null)
    {
        this.store = store;
        this.Themes = themes;
        this.Git = git;
        this.AppVersion = appVersion;
        this.Updater = new UpdaterViewModel(this, updates ?? new NoUpdateService());
        this.CommandLog = new CommandLogViewModel(git, this) { IsOpen = store.Settings.CommandLogOpen };
        this.CommandLog.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CommandLogViewModel.IsOpen))
            {
                this.store.Settings.CommandLogOpen = this.CommandLog.IsOpen;
                this.store.Save();
            }
        };
        this.Tabs.CollectionChanged += (_, _) =>
        {
            this.OnPropertyChanged(nameof(this.ShowWelcome));
            this.OnPropertyChanged(nameof(this.HasTabs));
            this.SaveTabs();
        };
        this.ThemeOptions = ThemeManager.Choices.Select(c => new ThemeOption(c, this)).ToList();
        themes.Changed += () =>
        {
            this.OnPropertyChanged(nameof(this.ThemeChoice));
            foreach (var option in this.ThemeOptions) option.Refresh();
        };
    }

    /// <summary>The welcome screen's theme buttons.</summary>
    public IReadOnlyList<ThemeOption> ThemeOptions { get; }

    public ThemeManager Themes { get; }

    public GitRunner Git { get; }

    /// <summary>Installed merge tools for the conflict dialog. Tests replace it with fakes.</summary>
    public Core.Platform.MergeToolRegistry MergeTools { get; set; } = new();

    public string AppVersion { get; }

    public SettingsStore Store => this.store;

    public AppSettings Settings => this.store.Settings;

    /// <summary>Set by the window; null in tests that do not need pickers or the clipboard.</summary>
    public IPlatformServices? Platform { get; set; }

    public ObservableCollection<RepoTabViewModel> Tabs { get; } = [];

    public ObservableCollection<AlertViewModel> Alerts { get; } = [];

    public CommandLogViewModel CommandLog { get; }

    public UpdaterViewModel Updater { get; }

    public bool ShowWelcome => this.Tabs.Count == 0;

    public bool HasTabs => this.Tabs.Count > 0;

    public IReadOnlyList<string> ThemeChoices => ThemeManager.Choices;

    public string ThemeChoice => this.Themes.Choice;

    [ObservableProperty]
    public partial RepoTabViewModel? SelectedTab { get; set; }

    /// <summary>The modal dialog shown over the window, or null.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDialog))]
    public partial DialogViewModel? Dialog { get; set; }

    public bool HasDialog => this.Dialog is not null;

    [ObservableProperty]
    public partial bool OpeningRepository { get; private set; }

    partial void OnSelectedTabChanged(RepoTabViewModel? oldValue, RepoTabViewModel? newValue)
    {
        if (oldValue is not null) oldValue.IsActive = false;
        if (newValue is not null) newValue.IsActive = true;
        this.SaveTabs();
    }

    /// <summary>Opens the repositories of the last session (and <paramref name="extra"/>, e.g. from the command line).</summary>
    public async Task RestoreAsync(IEnumerable<string> extra)
    {
        this.restoring = true;
        try
        {
            var active = this.Settings.ActiveIndex;
            foreach (var path in this.Settings.OpenRepositories.ToList()) this.AddTab(path);
            if (this.Tabs.Count > 0) this.SelectedTab = this.Tabs[Math.Clamp(active, 0, this.Tabs.Count - 1)];
            foreach (var path in extra) this.SelectedTab = this.Tabs.FirstOrDefault(t => SamePath(t.Path, path)) ?? this.AddTab(path);
        }
        finally
        {
            this.restoring = false;
        }
        this.SaveTabs();
        await Task.WhenAll(this.Tabs.Select(t => t.OpenAsync()));
    }

    /// <summary>Opens <paramref name="path"/> in a new tab, or switches to it if it is open.</summary>
    public async Task OpenRepositoryAsync(string path)
    {
        if (this.Tabs.FirstOrDefault(t => SamePath(t.Path, path)) is { } existing)
        {
            this.SelectedTab = existing;
            return;
        }
        var tab = this.AddTab(path);
        this.SelectedTab = tab;
        await tab.OpenAsync();
        if (tab.Session is null && tab.OpenError is { } error)
        {
            // Not a repository (or git is missing): report it instead of keeping a dead tab.
            this.CloseTab(tab);
            this.ShowError(error);
        }
        else if (this.Tabs.FirstOrDefault(t => t != tab && SamePath(t.Path, tab.Path)) is { } same)
        {
            // A folder inside a repository that is already open.
            this.CloseTab(tab);
            this.SelectedTab = same;
        }
        else
        {
            this.SaveTabs(); // The path may have become the repository's root.
        }
    }

    private RepoTabViewModel AddTab(string path)
    {
        var tab = new RepoTabViewModel(Path.GetFullPath(path), this);
        this.Tabs.Add(tab);
        return tab;
    }

    [RelayCommand]
    private async Task BrowseRepository()
    {
        if (await this.PickFolderAsync("Open a repository") is not { } folder) return;
        this.OpeningRepository = true;
        try
        {
            await this.OpenRepositoryAsync(folder);
        }
        finally
        {
            this.OpeningRepository = false;
        }
    }

    [RelayCommand]
    private void ShowCreateRepository() => this.ShowDialog(new CreateRepositoryDialogViewModel(this.Settings.LastParentFolder));

    [RelayCommand]
    private void ShowCloneRepository() => this.ShowDialog(new CloneRepositoryDialogViewModel(this.Settings.LastParentFolder));

    [RelayCommand]
    private void ShowAbout() =>
        this.ShowDialog(new AboutDialogViewModel(this.AppVersion, this.SelectedTab?.Session?.Repository.Version.ToString() ?? ""));

    /// <summary>
    /// Captures the window and the command log for a problem report, both
    /// anonymized and as they are, then lets the user look before saving.
    /// </summary>
    [RelayCommand]
    private async Task ReportProblem()
    {
        if (this.Platform is not { } platform) return;
        var anonymizer = ReportAnonymizer.Create(this);
        // Before the dialog opens, so the window is captured as the user saw it.
        var anonymizedPng = await platform.CaptureWindowAsync(anonymizer);
        var plainPng = await platform.CaptureWindowAsync(null);
        this.CommandLog.AddPrivateNames(anonymizer); // the log may have grown while the window was drawn
        var anonymized = new ProblemReport(anonymizedPng, this.CommandLog.ExportText(anonymizer), ProblemReport.EnvironmentText(this, anonymized: true));
        var plain = new ProblemReport(plainPng, this.CommandLog.ExportText(null), ProblemReport.EnvironmentText(this, anonymized: false));
        this.ShowDialog(new ProblemReportDialogViewModel(anonymized, plain, platform));
    }

    [RelayCommand]
    private void ShowSettings() => this.ShowDialog(new SettingsDialogViewModel(this.store, this.Themes, this.Updater.CanCheck));

    [RelayCommand]
    public void CloseTab(RepoTabViewModel tab)
    {
        var index = this.Tabs.IndexOf(tab);
        if (index < 0) return;
        var wasSelected = this.SelectedTab == tab;
        this.Tabs.RemoveAt(index);
        if (wasSelected) this.SelectedTab = this.Tabs.Count == 0 ? null : this.Tabs[Math.Max(0, index - 1)];
        tab.Dispose();
    }

    [RelayCommand]
    private void SelectTab(RepoTabViewModel tab) => this.SelectedTab = tab;

    public void MoveTab(int from, int to)
    {
        if (from == to || from < 0 || to < 0 || from >= this.Tabs.Count || to >= this.Tabs.Count) return;
        var selected = this.SelectedTab;
        this.Tabs.Move(from, to);
        this.SelectedTab = selected;
    }

    [RelayCommand]
    private void SelectTheme(string choice)
    {
        this.Themes.Select(choice);
        this.Settings.Theme = this.Themes.Choice;
        this.store.Save();
    }

    public void PreviewTheme(string? choice) => this.Themes.Preview(choice);

    public void SetDiffViewMode(DiffViewMode mode)
    {
        this.Settings.DiffViewMode = mode;
        this.store.Save();
        foreach (var tab in this.Tabs)
        {
            tab.Diff.ViewMode = mode;
            tab.History.Diff.ViewMode = mode;
            tab.Stash.Diff.ViewMode = mode;
        }
    }

    public void RememberParentFolder(string folder)
    {
        this.Settings.LastParentFolder = folder;
        this.store.Save();
    }

    public void SaveLayout(double leftPanelWidth, double commitFormHeight, double commandLogHeight)
    {
        this.Settings.LeftPanelWidth = leftPanelWidth;
        this.Settings.CommitFormHeight = commitFormHeight;
        this.Settings.CommandLogHeight = commandLogHeight;
        this.store.Save();
    }

    private void SaveTabs()
    {
        if (this.restoring) return;
        this.Settings.OpenRepositories = this.Tabs.Select(t => t.Path).ToList();
        this.Settings.ActiveIndex = this.SelectedTab is null ? 0 : Math.Max(0, this.Tabs.IndexOf(this.SelectedTab));
        this.store.Save();
    }

    /// <summary>Dialogs covered by a newer one (an askpass question while Add remote waits on git).</summary>
    private readonly List<DialogViewModel> coveredDialogs = [];

    private void ShowDialog(DialogViewModel dialog)
    {
        dialog.Owner = this;
        if (this.Dialog is { } covered) this.coveredDialogs.Add(covered);
        this.Dialog = dialog;
    }

    /// <summary>Closes <paramref name="dialog"/>; the one it covered, if any, shows again.</summary>
    internal void CloseDialog(DialogViewModel dialog)
    {
        if (this.Dialog != dialog)
        {
            this.coveredDialogs.Remove(dialog);
            return;
        }
        this.Dialog = this.coveredDialogs.Count == 0 ? null : this.coveredDialogs[^1];
        if (this.coveredDialogs.Count > 0) this.coveredDialogs.RemoveAt(this.coveredDialogs.Count - 1);
    }

    // ── AppImage desktop entry (the old app's desktop_entry) ──────────

    /// <summary>The running AppImage, or null (packages and other systems). Tests set it.</summary>
    internal string? AppImagePath { get; set; } = DesktopEntry.AppImagePath;

    internal string DataHome { get; set; } = DesktopEntry.DataHome;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRegisterDesktopEntry))]
    public partial DesktopEntryStatus DesktopEntryStatus { get; private set; }

    /// <summary>The options menu offers it while the launcher lacks this AppImage.</summary>
    public bool CanRegisterDesktopEntry => this.DesktopEntryStatus is DesktopEntryStatus.Missing or DesktopEntryStatus.Stale;

    /// <summary>On start: an AppImage that is not in the launcher asks once (until dismissed).</summary>
    public void CheckDesktopEntry()
    {
        this.DesktopEntryStatus = DesktopEntry.Check(this.AppImagePath, this.DataHome);
        if (!this.CanRegisterDesktopEntry || this.Settings.DesktopEntryDismissed) return;
        var (message, label) = this.DesktopEntryStatus == DesktopEntryStatus.Missing
            ? ("Add CheeseGit to your applications menu?", "Add")
            : ("The applications menu starts a different CheeseGit file. Point it to this one?", "Update");
        this.ShowInfo(message, label, () => { this.RegisterDesktopEntry(); return Task.CompletedTask; }, sticky: true, onDismissed: () =>
        {
            this.Settings.DesktopEntryDismissed = true;
            this.store.Save();
        });
    }

    [RelayCommand]
    private void RegisterDesktopEntry()
    {
        if (this.AppImagePath is not { } appImage) return;
        try
        {
            using var icon = Avalonia.Platform.AssetLoader.Open(new Uri("avares://CheeseGit/Assets/icon.png"));
            using var bytes = new MemoryStream();
            icon.CopyTo(bytes);
            DesktopEntry.Register(appImage, this.DataHome, bytes.ToArray());
            this.DesktopEntryStatus = DesktopEntry.Check(appImage, this.DataHome);
            this.ShowInfo("CheeseGit is in your applications menu.");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            this.ShowError($"Could not add CheeseGit to the applications menu: {e.Message}");
        }
    }

    // ── Askpass (docs/architecture.md, Askpass) ───────────────────────────────────────────

    private readonly SemaphoreSlim askpassQueue = new(1, 1);
    private readonly Dictionary<string, string> rememberedAnswers = new(StringComparer.Ordinal);
    private readonly List<(string Prompt, DateTimeOffset When)> servedAnswers = [];

    public AskpassServer? Askpass { get; private set; }

    /// <summary>Routes git's and ssh's questions to dialogs; <paramref name="helperPath"/> is this executable.</summary>
    public void EnableAskpass(string helperPath)
    {
        this.Askpass ??= new AskpassServer(helperPath, (request, ct) => Dispatcher.UIThread.InvokeAsync(() => this.AskAsync(request, ct)));
        this.Askpass.Configure(this.Git);
    }

    /// <summary>One question at a time (two fetches may both ask); a remembered answer is given without asking.</summary>
    internal async Task<string?> AskAsync(AskpassRequest request, CancellationToken ct)
    {
        await this.askpassQueue.WaitAsync(ct);
        try
        {
            if (request.Kind != AskpassKind.Confirm && this.rememberedAnswers.TryGetValue(request.Prompt, out var known))
            {
                this.servedAnswers.Add((request.Prompt, DateTimeOffset.Now));
                return known;
            }
            var dialog = new AskpassDialogViewModel(request);
            this.ShowDialog(dialog);
            using var _ = ct.Register(() => Dispatcher.UIThread.Post(dialog.Withdraw));
            if (!await dialog.Result) return null;
            if (request.Kind == AskpassKind.Confirm) return "yes";
            if (dialog.Remember) this.rememberedAnswers[request.Prompt] = dialog.Answer;
            return dialog.Answer;
        }
        finally
        {
            this.askpassQueue.Release();
        }
    }

    /// <summary>
    /// An operation failed: forget the remembered answers it used, so a wrong password is asked
    /// for again instead of being repeated. (Which question failed is only in git's messages.)
    /// </summary>
    internal void ForgetAnswersUsedSince(DateTimeOffset start)
    {
        foreach (var (prompt, _) in this.servedAnswers.Where(s => s.When >= start)) this.rememberedAnswers.Remove(prompt);
        this.servedAnswers.RemoveAll(s => s.When >= start);
    }

    // ── IAppShell ─────────────────────────────────────────────────────

    public void ShowError(string message)
    {
        // The same failure repeated (e.g. a refresh failing on every event) shows once.
        if (this.Alerts.Any(a => a.IsError && a.Message == message)) return;
        this.Alerts.Add(new AlertViewModel(this, message, isError: true, null, null));
    }

    /// <summary>How long a transient info alert stays. One per commit piled up under the title bar.</summary>
    internal static TimeSpan InfoLifetime { get; set; } = TimeSpan.FromSeconds(15);

    public void ShowInfo(string message, string? actionLabel = null, Func<Task>? action = null, bool sticky = false, Action? onDismissed = null)
    {
        foreach (var old in this.Alerts.Where(a => a.IsInfo && !a.Sticky).ToList()) this.Alerts.Remove(old);
        var alert = new AlertViewModel(this, message, isError: false, actionLabel, action) { Sticky = sticky, OnDismissed = onDismissed };
        this.Alerts.Add(alert);
        if (!sticky) DispatcherTimer.RunOnce(() => this.Alerts.Remove(alert), InfoLifetime);
    }

    public void ShowWhatsNew(string version, string notesMarkdown) => this.ShowDialog(new WhatsNewDialogViewModel(version, notesMarkdown));

    public Task OpenUrlAsync(string url) => this.Platform?.OpenUrlAsync(url) ?? Task.CompletedTask;

    public Task<bool> ConfirmAsync(string message, string confirmLabel, bool destructive = true)
    {
        var dialog = new ConfirmDialogViewModel(message, confirmLabel, destructive);
        this.ShowDialog(dialog);
        return dialog.Result;
    }

    public Task<bool> ShowDialogAsync(DialogViewModel dialog)
    {
        this.ShowDialog(dialog);
        return dialog.Result;
    }

    public Task<string?> PickFolderAsync(string title) => this.Platform?.PickFolderAsync(title) ?? Task.FromResult<string?>(null);

    public Task SetClipboardTextAsync(string text) => this.Platform?.SetClipboardTextAsync(text) ?? Task.CompletedTask;

    public Task SaveTextFileAsync(string title, string suggestedName, string text) =>
        this.Platform?.SaveTextFileAsync(title, suggestedName, text) ?? Task.CompletedTask;

    private static bool SamePath(string a, string b) =>
        string.Equals(
            Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar),
            OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);

    public void Dispose()
    {
        foreach (var tab in this.Tabs) tab.Dispose();
        this.CommandLog.Dispose();
        this.Askpass?.Dispose();
    }
}

public sealed partial class ThemeOption(string name, MainViewModel owner) : ObservableObject
{
    public string Name { get; } = name;

    public bool IsSelected => owner.Themes.Choice == this.Name;

    internal void Refresh() => this.OnPropertyChanged(nameof(this.IsSelected));

    [RelayCommand]
    private void Select() => owner.SelectThemeCommand.Execute(this.Name);
}
