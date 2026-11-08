using System.Collections.ObjectModel;
using CheeseGit.Core.Git;
using CheeseGit.Core.Platform;
using CheeseGit.Core.Repository;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CheeseGit.App.ViewModels;

/// <summary>One conflicted file in the conflict dialog (old app's ConflictFileRow).</summary>
public sealed partial class ConflictFileRow(ConflictDialogViewModel owner, GitPath path, int count, bool canMergeLines = true, bool isLfs = false) : ObservableObject
{
    /// <summary>
    /// Text: "Both" (a line union) and merge tools apply. Not for binary and LFS files, where a union
    /// is garbage (or a broken pointer) and a tool would see bytes or pointer lines.
    /// </summary>
    public bool CanMergeLines { get; } = canMergeLines;

    public bool IsLfs { get; } = isLfs;

    public GitPath Path { get; } = path;

    public string DisplayPath { get; } = DisplayText.Path(path);

    public string FileName { get; } = DisplayText.Name(path);

    /// <summary>The folder part, with its trailing slash; empty at the top level.</summary>
    public string Directory { get; } = DisplayText.Path(path) is var p && p.LastIndexOf('/') is var slash and >= 0 ? p[..(slash + 1)] : "";

    /// <summary>
    /// Markers when the dialog first saw the file. A file without markers from the start (binary,
    /// modify/delete) is not "resolved" by having none: it needs a choice.
    /// </summary>
    public int InitialCount { get; } = count;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountText), nameof(CountToolTip))]
    public partial int Count { get; set; } = count;

    public string CountText => this.Count > 0 ? this.Count.ToString() : "!";

    public string CountToolTip => this.Count switch
    {
        0 when this.IsLfs => "A Git LFS file: choose the version to keep",
        0 => "No conflict markers in the file (binary, deleted on one side, or already edited): choose the version to keep",
        1 => "1 conflicting region",
        var n => $"{n} conflicting regions",
    };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCurrent), nameof(IsIncoming), nameof(IsBoth), nameof(IsHandled))]
    public partial ConflictResolution? Resolution { get; set; }

    public bool IsCurrent => this.Resolution == ConflictResolution.AcceptCurrent;

    public bool IsIncoming => this.Resolution == ConflictResolution.AcceptIncoming;

    public bool IsBoth => this.Resolution == ConflictResolution.AcceptBoth;

    /// <summary>The markers are gone from the working tree (edited or merged in a tool): completing stages the file as it is.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsResolvedOutside), nameof(IsOpen), nameof(IsHandled))]
    public partial bool EditedOutside { get; set; }

    /// <summary>Staged outside the app (<c>git add</c>): git no longer lists it as conflicted.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsResolvedOutside), nameof(IsOpen), nameof(IsHandled))]
    public partial bool StagedOutside { get; set; }

    public bool IsResolvedOutside => this.EditedOutside || this.StagedOutside;

    public bool IsOpen => !this.IsResolvedOutside;

    public bool IsHandled => this.IsResolvedOutside || this.Resolution is not null;

    public ConflictDialogViewModel Owner => owner;

    // A second click on the chosen side clears it (old app).
    [RelayCommand]
    private void ChooseCurrent() => this.Choose(ConflictResolution.AcceptCurrent);

    [RelayCommand]
    private void ChooseIncoming() => this.Choose(ConflictResolution.AcceptIncoming);

    [RelayCommand]
    private void ChooseBoth() => this.Choose(ConflictResolution.AcceptBoth);

    private void Choose(ConflictResolution resolution)
    {
        this.Resolution = this.Resolution == resolution ? null : resolution;
        owner.OnRowChanged();
    }

    [RelayCommand]
    private Task OpenInTool() => owner.OpenInToolAsync(this);
}

/// <summary>A merge tool in the dialog's tool picker.</summary>
public sealed partial class MergeToolChoice(ConflictDialogViewModel owner, MergeTool tool, bool isSelected) : ObservableObject
{
    public MergeTool Tool { get; } = tool;

    public string Name => this.Tool.DisplayName;

    public bool IsSelected { get; } = isSelected;

    [RelayCommand]
    private void Select() => owner.SelectTool(this.Tool);
}

/// <summary>
/// Resolves the conflicts of a paused merge, revert or cherry-pick (old app's MergeConflictDialog):
/// per file keep the current side, the incoming side or both, or merge it in an external tool; then
/// commit or abort. Closing it leaves the operation paused; the banner opens it again. Files the
/// user fixes elsewhere are noticed on every status refresh (the old app polled every 2 s).
/// </summary>
public sealed partial class ConflictDialogViewModel : DialogViewModel
{
    private readonly RepoTabViewModel tab;

    public ConflictDialogViewModel(RepoTabViewModel tab, OperationState state)
    {
        this.tab = tab;
        this.Kind = state.Kind;
        this.Incoming = state.Incoming;
        this.Message = this.DefaultMessage;
        this.UpdateTools();
    }

    public OperationKind Kind { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Subtitle))]
    public partial string Incoming { get; private set; }

    private string Verb => this.Kind switch
    {
        OperationKind.Revert => "Revert",
        OperationKind.CherryPick => "Cherry-pick",
        _ => "Merge",
    };

    public string Title => $"{this.Verb} conflicts";

    public string Subtitle => this.Kind switch
    {
        OperationKind.Revert => $"Reverting {this.Incoming}",
        OperationKind.CherryPick => $"Cherry-picking {this.Incoming}",
        _ => $"Merging {this.Incoming} into {this.tab.Snapshot?.Head.Branch ?? "the current branch"}",
    };

    public string AbortLabel => $"Abort {this.Verb.ToLowerInvariant()}";

    public string CompleteLabel => this.Busy ? "Completing…" : $"Complete {this.Verb.ToLowerInvariant()}";

    private string DefaultMessage => this.Kind switch
    {
        OperationKind.Revert => $"Revert \"{this.Incoming}\"",
        OperationKind.CherryPick => $"Cherry-pick {this.Incoming}",
        _ => $"Merge branch '{this.Incoming}'",
    };

    public ObservableCollection<ConflictFileRow> Files { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NoFiles))]
    public partial bool Loaded { get; private set; }

    /// <summary>Everything was resolved before the dialog opened (e.g. staged in a terminal): only the commit is left.</summary>
    public bool NoFiles => this.Loaded && this.Files.Count == 0;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CompleteCommand))]
    public partial string Message { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CompleteLabel))]
    [NotifyCanExecuteChangedFor(nameof(CompleteCommand), nameof(AbortCommand))]
    public partial bool Busy { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? Error { get; private set; }

    public bool HasError => this.Error is not null;

    // ── Merge tools ──────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTool), nameof(NoTool), nameof(OpenLabel))]
    public partial MergeTool? Tool { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasManyTools))]
    public partial IReadOnlyList<MergeToolChoice> ToolChoices { get; private set; } = [];

    public bool HasTool => this.Tool is not null;

    public bool NoTool => this.Tool is null;

    public bool HasManyTools => this.ToolChoices.Count > 1;

    public string OpenLabel => this.Tool is { } t ? $"Open in {t.DisplayName}" : "No merge tool";

    public string NoToolTip => "No supported merge tool found (VS Code, Rider, IntelliJ, Meld, KDiff3, Beyond Compare, Vim)";

    /// <summary>Raised when a tool was picked, so the view closes the picker.</summary>
    public event Action? ToolPickerCloseRequested;

    private void UpdateTools()
    {
        var registry = this.tab.MainShell.MergeTools;
        var tool = registry.Pick(this.tab.MainShell.Settings.PreferredMergeTool);
        this.Tool = tool;
        this.ToolChoices = [.. registry.Available().Select(t => new MergeToolChoice(this, t, t.Id == tool?.Id))];
    }

    internal void SelectTool(MergeTool tool)
    {
        var shell = this.tab.MainShell;
        shell.Settings.PreferredMergeTool = tool.Id;
        shell.Store.Save();
        this.UpdateTools();
        this.ToolPickerCloseRequested?.Invoke();
    }

    internal async Task OpenInToolAsync(ConflictFileRow row)
    {
        if (this.Tool is not { } tool) return;
        this.Error = null;
        // Reads the index stages and starts the tool; it does not wait for the tool to close.
        var started = await this.tab.ReadAsync($"Open {row.DisplayPath} in {tool.DisplayName:public}", async repo =>
            await this.tab.MainShell.MergeTools.OpenAsync(tool, repo, row.Path));
        if (started is null) this.Error = $"Could not open {row.DisplayPath} in {tool.DisplayName}.";
    }

    // ── Loading and refreshing ───────────────────────────────────────

    /// <summary>
    /// Set once git's prepared message is in: until then a refresh (a snapshot arriving) must not
    /// mark the dialog loaded, or Complete could commit the placeholder message.
    /// </summary>
    private bool messageRead;

    public async Task LoadAsync()
    {
        this.messageRead = false;
        var prepared = await this.tab.ReadAsync("Read merge message", async repo => await repo.PreparedMessageAsync() ?? "");
        if (prepared is { Length: > 0 } && this.Message == this.DefaultMessage) this.Message = prepared;
        this.messageRead = true;
        await this.RefreshAsync();
    }

    /// <summary>Re-reads the conflicts: files resolved or staged elsewhere are marked, new ones added.</summary>
    public async Task RefreshAsync()
    {
        if (this.Busy || !this.messageRead) return;
        var counts = await this.tab.ReadAsync("List conflicts", repo => repo.ConflictCountsAsync());
        if (counts is null || this.Busy) return;
        var byPath = counts.ToDictionary(c => c.Path);
        foreach (var row in this.Files)
        {
            if (byPath.Remove(row.Path, out var info))
            {
                row.StagedOutside = false;
                row.Count = info.ConflictCount;
                row.EditedOutside = row.InitialCount > 0 && info.ConflictCount == 0;
            }
            else
            {
                row.StagedOutside = true;
            }
        }
        foreach (var info in counts.Where(c => byPath.ContainsKey(c.Path)))
        {
            this.Files.Add(new ConflictFileRow(this, info.Path, info.ConflictCount, info.CanMergeLines, info.IsLfs));
        }
        this.Loaded = true;
        this.OnPropertyChanged(nameof(this.NoFiles));
        this.OnRowChanged();
    }

    internal void OnRowChanged() => this.CompleteCommand.NotifyCanExecuteChanged();

    // ── Completing and aborting ──────────────────────────────────────

    private bool CanComplete() => !this.Busy && this.Loaded && this.Message.Trim().Length > 0 && this.Files.All(f => f.IsHandled);

    [RelayCommand(CanExecute = nameof(CanComplete))]
    private async Task Complete()
    {
        this.Busy = true;
        this.CanClose = false;
        this.Error = null;
        var kind = this.Kind;
        var message = this.Message.Trim();
        var plan = this.Files
            .Where(f => !f.StagedOutside)
            .Select(f => (f.Path, Resolution: f.EditedOutside ? ConflictResolution.KeepWorkingTree : f.Resolution!.Value))
            .ToList();
        var finished = true;
        var done = await this.tab.RunAsync($"Complete {this.Verb.ToLowerInvariant():public}", async (repo, ct) =>
        {
            foreach (var (path, resolution) in plan) await repo.ResolveConflictAsync(path, resolution, ct);
            finished = await repo.ContinueOperationAsync(kind, message, ct);
        });
        this.Busy = false;
        this.CanClose = true;
        if (!done)
        {
            // The resolutions that were applied stay applied; the refresh shows what is left.
            await this.RefreshAsync();
            return;
        }
        if (!finished && this.tab.Session?.Snapshot.Operation is { } next)
        {
            // A later commit of the cherry-pick sequence conflicts too: the dialog moves on to it.
            this.Incoming = next.Incoming;
            this.Message = this.DefaultMessage;
            this.Files.Clear();
            this.Loaded = false;
            await this.LoadAsync();
            return;
        }
        this.tab.MainShell.ShowInfo(kind switch
        {
            OperationKind.Revert => $"Reverted {this.Incoming}",
            OperationKind.CherryPick => $"Cherry-picked {this.Incoming}",
            _ => $"Merged {this.Incoming}",
        });
        this.Close(true);
    }

    private bool CanAbort() => !this.Busy;

    [RelayCommand(CanExecute = nameof(CanAbort))]
    private async Task Abort()
    {
        this.Busy = true;
        this.CanClose = false;
        var kind = this.Kind;
        var done = await this.tab.RunAsync($"Abort {this.Verb.ToLowerInvariant():public}", (repo, ct) => repo.AbortOperationAsync(kind, ct));
        this.Busy = false;
        this.CanClose = true;
        if (done) this.Close(false);
    }
}
