using CheeseGit.Core.Git;
using CheeseGit.Core.Repository;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CheeseGit.App.ViewModels;

/// <summary>A local branch in the cherry-pick target picker.</summary>
public sealed partial class CherryPickTarget(CherryPickDialogViewModel owner, BranchInfo branch)
{
    public string Name => branch.Name;

    public bool IsCurrent => branch.IsCurrent;

    [RelayCommand]
    private void Choose() => owner.Choose(this.Name, create: false);
}

/// <summary>
/// "Cherry-pick N commits to…" (old app's CherryPickBranchPicker): find a local branch, or name a new
/// one, which is created at HEAD. The picks themselves run after the dialog closes.
/// </summary>
public sealed partial class CherryPickDialogViewModel : DialogViewModel
{
    private IReadOnlyList<CherryPickTarget> all = [];

    public CherryPickDialogViewModel(IReadOnlyList<Oid> commits) => this.Commits = commits;

    /// <summary>Oldest first, the order they are picked in.</summary>
    public IReadOnlyList<Oid> Commits { get; }

    public string Title => $"Cherry-pick {this.Commits.Count} {(this.Commits.Count == 1 ? "commit" : "commits")} to…";

    /// <summary>The branch picked, and whether it is to be created; null until one is.</summary>
    public (string Branch, bool Create)? Choice { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCreate), nameof(CreateLabel), nameof(NoMatches), nameof(NoMatchesText))]
    public partial string Query { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NoMatches), nameof(CanCreate))]
    public partial IReadOnlyList<CherryPickTarget> Branches { get; private set; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NoMatches))]
    public partial bool Loading { get; private set; } = true;

    public bool NoMatches => !this.Loading && this.Branches.Count == 0;

    public string NoMatchesText => this.Query.Trim().Length > 0 ? $"No branch matching \"{this.Query.Trim()}\"" : "No branches";

    public bool CanCreate => this.Query.Trim() is { Length: > 0 } name && !this.all.Any(b => string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase));

    public string CreateLabel => $"Create and cherry-pick to \"{this.Query.Trim()}\"";

    public async Task LoadAsync(RepoTabViewModel tab)
    {
        var branches = await tab.ReadAsync("List branches", repo => repo.ListBranchesAsync());
        this.all = [.. (branches ?? []).Select(b => new CherryPickTarget(this, b))];
        this.Loading = false;
        this.Filter();
    }

    partial void OnQueryChanged(string value) => this.Filter();

    private void Filter()
    {
        var query = this.Query.Trim();
        this.Branches = query.Length == 0 ? this.all : [.. this.all.Where(b => b.Name.Contains(query, StringComparison.OrdinalIgnoreCase))];
    }

    internal void Choose(string branch, bool create)
    {
        this.Choice = (branch, create);
        this.Close(true);
    }

    [RelayCommand]
    private void Create()
    {
        if (this.CanCreate) this.Choose(this.Query.Trim(), create: true);
    }
}
