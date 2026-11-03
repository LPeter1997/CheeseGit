using CheeseGit.Core.Diff;
using CheeseGit.Core.Git;
using CheeseGit.Core.Repository;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CheeseGit.App.ViewModels;

/// <summary>
/// The files of one change set (a commit in the history, a stash entry) with their stats, and the
/// diff of the selected file: the right-hand panel of the History and Stash tabs (old app's
/// CommitDiffPanel and StashDiffPanel, which were the same layout).
/// </summary>
public sealed partial class ChangeDetailsViewModel(RepoTabViewModel tab, DiffViewModel diff, string noSubjectText) : ObservableObject
{
    private int version;
    private Func<GitRepository, GitPath, Task<FileDiff>>? diffOf;
    private object? subject;

    public DiffViewModel Diff { get; } = diff;

    /// <summary>"Select a commit to view its changes."</summary>
    public string NoSubjectText { get; } = noSubjectText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNoSubject))]
    public partial bool HasSubject { get; private set; }

    public bool ShowNoSubject => !this.HasSubject;

    public BulkObservableCollection<FileRow> Files { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNoFiles))]
    public partial bool FilesLoading { get; private set; }

    [ObservableProperty]
    public partial double AdditionsWidth { get; private set; }

    [ObservableProperty]
    public partial double DeletionsWidth { get; private set; }

    public bool ShowNoFiles => this.HasSubject && !this.FilesLoading && this.Files.Count == 0;

    /// <summary>
    /// Shows <paramref name="key"/>'s files (listed by <paramref name="read"/>); a selected file's
    /// diff comes from <paramref name="diffOf"/>. Loading the same subject again keeps the selection.
    /// </summary>
    public async void Load(object key, string label, Func<GitRepository, Task<CommitFileChanges>> read, Func<GitRepository, GitPath, Task<FileDiff>> diffOf)
    {
        if (Equals(key, this.subject) && this.HasSubject) return;
        var version = ++this.version;
        this.subject = key;
        this.diffOf = diffOf;
        this.HasSubject = true;
        this.FilesLoading = true;
        this.Files.ReplaceAll([]);
        this.Diff.Clear();
        var changes = await tab.ReadAsync(label, read);
        if (version != this.version) return;
        this.FilesLoading = false;
        if (changes is null)
        {
            this.OnPropertyChanged(nameof(this.ShowNoFiles));
            return;
        }

        var stats = changes.Stats.GroupBy(s => s.Path).ToDictionary(g => g.Key, g => g.Last());
        var rows = changes.Files.Select(f => new FileRow(f, DiffArea.Staged) { Stats = f.IsLfs ? null : stats.GetValueOrDefault(f.Path) }).ToList();
        this.AdditionsWidth = StatWidth(rows.Max(r => (int?)r.AdditionsText.Length) ?? 0);
        this.DeletionsWidth = StatWidth(rows.Max(r => (int?)r.DeletionsText.Length) ?? 0);
        this.Files.ReplaceAll(rows);
        this.OnPropertyChanged(nameof(this.ShowNoFiles));
    }

    public void Clear()
    {
        this.version++;
        this.subject = null;
        this.diffOf = null;
        this.HasSubject = false;
        this.FilesLoading = false;
        this.Files.ReplaceAll([]);
        this.OnPropertyChanged(nameof(this.ShowNoFiles));
        this.Diff.Clear();
    }

    // Matches the staging lists' stat columns: about 7 px per character at 12 px.
    private static double StatWidth(int chars) => chars == 0 ? 0 : chars * 7 + 2;

    [RelayCommand]
    private Task CopyPath(FileRow file) => file.CopyPathAsync(tab.Shell);

    [RelayCommand]
    private Task CopyName(FileRow file) => file.CopyNameAsync(tab.Shell);

    public void SelectFile(FileRow file)
    {
        if (this.diffOf is not { } diffOf) return;
        foreach (var f in this.Files) f.IsCurrent = f == file;
        this.Diff.ShowLoaded(file.Path, file.DisplayPath, repo => diffOf(repo, file.Path));
    }

    /// <summary>Up/Down in the file list.</summary>
    public FileRow? MoveFile(int delta)
    {
        if (this.Files.Count == 0) return null;
        var current = this.Files.IndexOf(this.Files.FirstOrDefault(f => f.IsCurrent)!);
        var file = this.Files[current < 0 ? 0 : Math.Clamp(current + delta, 0, this.Files.Count - 1)];
        this.SelectFile(file);
        return file;
    }
}
