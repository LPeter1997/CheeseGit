using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CheeseGit.App.Services;
using CheeseGit.App.Theming;
using CheeseGit.Core.Git;
using CheeseGit.Core.Repository;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CheeseGit.App.ViewModels;

/// <summary>A banner under the title bar (the old app's AlertBanners).</summary>
public sealed partial class AlertViewModel(MainViewModel owner, string message, bool isError, string? actionLabel, Func<Task>? action) : ObservableObject
{
    public string Message { get; } = message;

    public bool IsError { get; } = isError;

    public bool IsInfo => !this.IsError;

    public string Icon => this.IsError ? "✕" : "ℹ";

    public string? ActionLabel { get; } = actionLabel;

    public bool HasAction => this.ActionLabel is not null;

    /// <summary>Stays until dismissed instead of giving way to the next info alert.</summary>
    public bool Sticky { get; init; }

    /// <summary>Runs when the user closes the alert (not when its action runs).</summary>
    public Action? OnDismissed { get; init; }

    [RelayCommand]
    private void Dismiss()
    {
        owner.Alerts.Remove(this);
        this.OnDismissed?.Invoke();
    }

    /// <summary>Shown for 1.5 s after copying (old app: a fading "Copied!" beside the button).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NotCopied))]
    public partial bool Copied { get; private set; }

    public bool NotCopied => !this.Copied;

    private IDisposable? copiedTimer;

    /// <summary>Snapshot mode: the copied state, without touching the clipboard.</summary>
    internal void ShowCopiedSample() => this.Copied = true;

    [RelayCommand]
    private async Task Copy()
    {
        await owner.SetClipboardTextAsync(this.Message);
        this.Copied = true;
        // Copying again restarts the 1.5 s instead of hiding the first one's label early.
        this.copiedTimer?.Dispose();
        this.copiedTimer = DispatcherTimer.RunOnce(() => this.Copied = false, TimeSpan.FromSeconds(1.5));
    }

    [RelayCommand]
    private async Task RunAction()
    {
        owner.Alerts.Remove(this);
        if (action is not null) await action();
    }
}

/// <summary>Base for the in-window modal dialogs; the window shows <see cref="MainViewModel.Dialog"/> over a backdrop.</summary>
public abstract partial class DialogViewModel : ObservableObject
{
    private readonly TaskCompletionSource<bool> result = new();

    /// <summary>False while the dialog runs something that must not be interrupted.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    public partial bool CanClose { get; protected set; } = true;

    public Task<bool> Result => this.result.Task;

    internal MainViewModel? Owner { get; set; }

    [RelayCommand(CanExecute = nameof(CanClose))]
    public virtual void Cancel() => this.Close(false);

    protected void Close(bool confirmed)
    {
        this.Owner?.CloseDialog(this);
        this.result.TrySetResult(confirmed);
    }
}

public sealed partial class ConfirmDialogViewModel(string message, string confirmLabel, bool destructive) : DialogViewModel
{
    public string Message { get; } = message;

    public string ConfirmLabel { get; } = confirmLabel;

    public bool Destructive { get; } = destructive;

    public bool NotDestructive => !this.Destructive;

    [RelayCommand]
    private void Confirm() => this.Close(true);
}

/// <summary>Asks whether local changes go along to the new branch or stay on the old one (#52).</summary>
public sealed partial class SwitchChangesDialogViewModel(string from, string to) : DialogViewModel
{
    public string From { get; } = from;

    public string To { get; } = to;

    public string Title => $"You have changes on {this.From}";

    public string LeaveLabel => $"Leave on {this.From}";

    public string BringLabel => $"Bring to {this.To}";

    /// <summary>The answer, once the dialog closed with one.</summary>
    public SwitchChanges? Choice { get; private set; }

    [ObservableProperty]
    public partial bool Remember { get; set; }

    [RelayCommand]
    private void Leave()
    {
        this.Choice = SwitchChanges.Leave;
        this.Close(true);
    }

    [RelayCommand]
    private void Bring()
    {
        this.Choice = SwitchChanges.Bring;
        this.Close(true);
    }
}

/// <summary>A question from git or ssh, through the askpass bridge (docs/architecture.md, Askpass).</summary>
public sealed partial class AskpassDialogViewModel(AskpassRequest request) : DialogViewModel
{
    public AskpassRequest Request { get; } = request;

    /// <summary>git's or ssh's own words: they name the host, the user or the key.</summary>
    public string Prompt { get; } = request.Prompt.Trim();

    public bool IsText => this.Request.Kind == AskpassKind.Text;

    public bool IsSecret => this.Request.Kind == AskpassKind.Secret;

    public bool IsConfirm => this.Request.Kind == AskpassKind.Confirm;

    public bool HasField => !this.IsConfirm;

    public string Title => this.IsConfirm
        ? this.Prompt.Contains("authenticity of host", StringComparison.Ordinal) ? "Unknown host" : "Confirm"
        : "Authentication required";

    public string SubmitLabel => this.IsConfirm ? "Continue" : "OK";

    [ObservableProperty]
    public partial string Answer { get; set; } = "";

    /// <summary>Kept in memory until CheeseGit exits; never written anywhere.</summary>
    [ObservableProperty]
    public partial bool Remember { get; set; }

    [RelayCommand]
    private void Submit() => this.Close(true);

    /// <summary>git stopped waiting (the operation was cancelled): the question goes away unanswered.</summary>
    internal void Withdraw() => this.Close(false);
}

/// <summary>
/// Report a problem: previews the screenshot and the command log taken when it
/// opened, anonymized unless the user turns that off, and saves them as a .zip.
/// </summary>
public sealed partial class ProblemReportDialogViewModel(ProblemReport anonymized, ProblemReport plain, IPlatformServices? platform) : DialogViewModel
{
    /// <summary>A long log as one text block took seconds to lay out; the saved file has all of it.</summary>
    internal const int PreviewLines = 200;

    private Bitmap? anonymizedImage;
    private Bitmap? plainImage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Report), nameof(Screenshot), nameof(HasScreenshot), nameof(LogPreview), nameof(EnvironmentText))]
    public partial bool Anonymized { get; set; } = true;

    public ProblemReport Report => this.Anonymized ? anonymized : plain;

    public Bitmap? Screenshot => this.Anonymized
        ? this.anonymizedImage ??= Decode(anonymized.Screenshot)
        : this.plainImage ??= Decode(plain.Screenshot);

    public bool HasScreenshot => this.Report.Screenshot is not null;

    public string LogPreview
    {
        get
        {
            var log = this.Report.Log;
            if (log.Length == 0) return "The command log is empty.";
            var lines = log.Split('\n');
            return lines.Length <= PreviewLines ? log : string.Join('\n', lines[..PreviewLines]) + $"\n… {lines.Length - PreviewLines} more lines in the saved report";
        }
    }

    public string EnvironmentText => this.Report.Environment;

    public string SuggestedName { get; } = $"cheesegit-report-{DateTime.Now:yyyyMMdd-HHmmss}.zip";

    [RelayCommand]
    private async Task Save()
    {
        if (platform is null) return;
        if (await platform.SaveFileAsync("Save problem report", this.SuggestedName, this.Report.ToZip())) this.Close(true);
    }

    private static Bitmap? Decode(byte[]? png)
    {
        if (png is null) return null;
        try
        {
            return new Bitmap(new MemoryStream(png));
        }
        catch (Exception)
        {
            return null; // The preview is a convenience; the report still saves.
        }
    }
}

public sealed partial class AboutDialogViewModel(string version, string gitVersion) : DialogViewModel
{
    public string Version { get; } = version;

    public string GitVersion { get; } = gitVersion;
}

/// <summary>Settings that are not a one-click toggle elsewhere (theme lives in the options menu too).</summary>
public sealed partial class SettingsDialogViewModel : DialogViewModel
{
    private readonly SettingsStore store;
    private readonly ThemeManager themes;

    public SettingsDialogViewModel(SettingsStore store, ThemeManager themes, bool canUpdate = false)
    {
        this.store = store;
        this.themes = themes;
        this.CanUpdate = canUpdate;
        this.CheckForUpdates = store.Settings.CheckForUpdates;
        this.Theme = themes.Choice;
        this.NativeWayland = store.Settings.NativeWayland;
        this.SplitDiff = store.Settings.DiffViewMode == DiffViewMode.Split;
        this.SwitchChanges = SwitchChoices[(int)store.Settings.SwitchChanges];
        var lfs = Array.IndexOf(LfsDownloadLimits, store.Settings.LfsAutoDownloadMegabytes);
        this.LfsDownload = LfsDownloadLabels[lfs < 0 ? Array.IndexOf(LfsDownloadLimits, 10) : lfs];
    }

    /// <summary>Choices for <see cref="AppSettings.LfsAutoDownloadMegabytes"/>; 32 MB is the largest image previewed.</summary>
    private static readonly int[] LfsDownloadLimits = [0, 1, 10, 32];

    public static string[] LfsDownloadLabels { get; } = ["Only when I ask", "Up to 1 MB", "Up to 10 MB", "Up to 32 MB"];

    public IReadOnlyList<string> LfsDownloadChoices => LfsDownloadLabels;

    [ObservableProperty]
    public partial string LfsDownload { get; set; }

    partial void OnLfsDownloadChanged(string value)
    {
        var index = Array.IndexOf(LfsDownloadLabels, value);
        if (index < 0) return;
        this.store.Settings.LfsAutoDownloadMegabytes = LfsDownloadLimits[index];
        this.store.Save();
    }

    /// <summary>Labels for <see cref="Services.SwitchChanges"/>, in its order.</summary>
    public static string[] SwitchChoices { get; } = ["Ask each time", "Bring them to the new branch", "Leave them on the old branch"];

    public IReadOnlyList<string> SwitchChangesChoices => SwitchChoices;

    [ObservableProperty]
    public partial string SwitchChanges { get; set; }

    partial void OnSwitchChangesChanged(string value)
    {
        var index = Array.IndexOf(SwitchChoices, value);
        if (index < 0) return;
        this.store.Settings.SwitchChanges = (Services.SwitchChanges)index;
        this.store.Save();
    }

    public IReadOnlyList<string> Themes => ThemeManager.Choices;

    public string ConfigFolder => SettingsStore.DefaultDirectory;

    public bool IsLinux => OperatingSystem.IsLinux();

    [ObservableProperty]
    public partial string Theme { get; set; }

    [ObservableProperty]
    public partial bool NativeWayland { get; set; }

    /// <summary>Only copies that can update (installed or distro builds) offer the startup check.</summary>
    public bool CanUpdate { get; }

    [ObservableProperty]
    public partial bool CheckForUpdates { get; set; }

    partial void OnCheckForUpdatesChanged(bool value)
    {
        this.store.Settings.CheckForUpdates = value;
        this.store.Save();
    }

    [ObservableProperty]
    public partial bool SplitDiff { get; set; }

    partial void OnThemeChanged(string value)
    {
        this.themes.Select(value);
        this.store.Settings.Theme = value;
        this.store.Save();
    }

    partial void OnNativeWaylandChanged(bool value)
    {
        this.store.Settings.NativeWayland = value;
        this.store.Save();
    }

    partial void OnSplitDiffChanged(bool value)
    {
        this.Owner?.SetDiffViewMode(value ? DiffViewMode.Split : DiffViewMode.Unified);
    }
}

/// <summary>Shared by the create and clone dialogs: a parent folder plus the new repository's folder name.</summary>
public abstract partial class NewRepositoryDialogViewModel : DialogViewModel
{
    protected NewRepositoryDialogViewModel(string? parentFolder) => this.ParentFolder = parentFolder ?? "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PathStatus), nameof(PathOk), nameof(PathError))]
    [NotifyCanExecuteChangedFor(nameof(SubmitCommand))]
    public partial string ParentFolder { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? Error { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SubmitCommand))]
    public partial bool Busy { get; set; }

    public bool HasError => this.Error is not null;

    /// <summary>The folder that will be created, or null while the inputs are incomplete.</summary>
    protected abstract string FolderName { get; }

    public string? TargetPath => this.ParentFolder.Trim().Length > 0 && this.FolderName.Length > 0
        ? Path.Combine(this.ParentFolder.Trim(), this.FolderName)
        : null;

    /// <summary>Why the target cannot be used, or null when it can (or the inputs are incomplete).</summary>
    public string? PathError
    {
        get
        {
            var parent = this.ParentFolder.Trim();
            var name = this.FolderName;
            if (parent.Length == 0 || name.Length == 0) return null;
            if (name.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) >= 0 || name is "." or "..")
            {
                return "The name cannot contain path separators.";
            }
            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return "The name contains characters that are not allowed in file names.";
            if (!Directory.Exists(parent)) return "The parent folder does not exist.";
            var target = Path.Combine(parent, name);
            if (File.Exists(target)) return "A file with this name already exists.";
            if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any()) return "A folder with this name already exists and is not empty.";
            return null;
        }
    }

    public bool PathOk => this.TargetPath is not null && this.PathError is null;

    public string PathStatus => this.PathError ?? (this.PathOk ? this.TargetPath! : "");

    protected void PathInputsChanged()
    {
        this.OnPropertyChanged(nameof(this.TargetPath));
        this.OnPropertyChanged(nameof(this.PathStatus));
        this.OnPropertyChanged(nameof(this.PathOk));
        this.OnPropertyChanged(nameof(this.PathError));
        this.SubmitCommand.NotifyCanExecuteChanged();
    }

    partial void OnParentFolderChanged(string value) => this.Error = null;

    [RelayCommand]
    private async Task Browse()
    {
        if (this.Owner is null) return;
        if (await this.Owner.PickFolderAsync("Choose the parent folder") is { } folder) this.ParentFolder = folder;
    }

    private bool CanSubmit() => this.PathOk && !this.Busy;

    [RelayCommand(CanExecute = nameof(CanSubmit))]
    private async Task Submit()
    {
        if (this.Owner is null || this.TargetPath is not { } target) return;
        this.Error = null;
        this.Busy = true;
        this.CanClose = false;
        try
        {
            var path = await this.CreateAsync(target, this.Owner.Git);
            this.Owner.RememberParentFolder(this.ParentFolder.Trim());
            this.CanClose = true;
            this.Close(true);
            await this.Owner.OpenRepositoryAsync(path);
        }
        catch (Exception e) when (e is GitException or IOException or UnauthorizedAccessException)
        {
            this.Error = e.Message;
        }
        catch (OperationCanceledException)
        {
            // Cancelled by the user.
        }
        finally
        {
            this.Busy = false;
            this.CanClose = true;
        }
    }

    /// <summary>Creates the repository and returns its root.</summary>
    protected abstract Task<string> CreateAsync(string target, GitRunner git);
}

public sealed partial class CreateRepositoryDialogViewModel(string? parentFolder) : NewRepositoryDialogViewModel(parentFolder)
{
    [ObservableProperty]
    public partial string Name { get; set; } = "";

    protected override string FolderName => this.Name.Trim();

    partial void OnNameChanged(string value)
    {
        this.Error = null;
        this.PathInputsChanged();
    }

    protected override async Task<string> CreateAsync(string target, GitRunner git)
    {
        using var scope = GitRunner.BeginOperation("Create repository");
        try
        {
            using var repo = await Task.Run(() => GitRepository.InitAsync(target, git));
            return repo.Root;
        }
        catch (Exception e)
        {
            scope.Fail(e);
            throw;
        }
    }
}

public sealed partial class CloneRepositoryDialogViewModel(string? parentFolder) : NewRepositoryDialogViewModel(parentFolder)
{
    private CancellationTokenSource? cancel;

    [ObservableProperty]
    public partial string Url { get; set; } = "";

    protected override string FolderName => this.Url.Trim().Length == 0 ? "" : GitRepository.DirectoryNameForUrl(this.Url.Trim());

    partial void OnUrlChanged(string value)
    {
        this.Error = null;
        this.PathInputsChanged();
    }

    /// <summary>Cancelling a running clone kills git; the dialog stays open so the user can retry.</summary>
    public override void Cancel()
    {
        if (this.Busy)
        {
            this.cancel?.Cancel();
            return;
        }
        base.Cancel();
    }

    protected override async Task<string> CreateAsync(string target, GitRunner git)
    {
        this.CanClose = true; // Cancel stays available: it stops the clone.
        using var cts = this.cancel = new CancellationTokenSource();
        using var scope = GitRunner.BeginOperation("Clone repository");
        try
        {
            using var repo = await Task.Run(() => GitRepository.CloneAsync(this.Url.Trim(), Path.GetDirectoryName(target)!, git, cts.Token));
            return repo.Root;
        }
        catch (OperationCanceledException)
        {
            // git may have left a partial clone behind.
            TryDelete(target);
            throw;
        }
        catch (Exception e)
        {
            scope.Fail(e);
            throw;
        }
        finally
        {
            this.cancel = null;
        }
    }

    private static void TryDelete(string folder)
    {
        try
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Leave it; the next attempt reports the folder as not empty.
        }
    }
}
