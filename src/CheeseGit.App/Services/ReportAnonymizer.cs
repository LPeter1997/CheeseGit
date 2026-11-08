using CheeseGit.App.ViewModels;
using CheeseGit.Core.Diagnostics;
using CheeseGit.Core.Git;

namespace CheeseGit.App.Services;

/// <summary>
/// Builds the <see cref="Anonymizer"/> for one problem report: every private
/// string the app can show is added to its dictionary, so the screenshot and
/// the command log replace them the same way.
/// </summary>
internal static class ReportAnonymizer
{
    public static Anonymizer Create(MainViewModel main)
    {
        var a = new Anonymizer();
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (home.Length > 0) a.AddPath(home);
        a.AddText(Environment.UserName);
        a.AddText(Environment.MachineName);

        var settings = main.Settings;
        foreach (var path in settings.OpenRepositories.Concat(settings.CommitGraphOffered)) a.AddPath(path);
        if (settings.LastParentFolder is { } parent) a.AddPath(parent);

        // The current state first: a name added as a ref keeps mapping as a ref when the log adds it again.
        foreach (var tab in main.Tabs) AddTab(a, tab);
        main.CommandLog.AddPrivateNames(a);
        return a;
    }

    private static void AddTab(Anonymizer a, RepoTabViewModel tab)
    {
        a.AddPath(tab.Path);
        if (tab.Session?.Repository is { } repo)
        {
            a.AddPath(repo.Root);
            a.AddPath(repo.GitDir);
            a.AddPath(repo.CommonDir);
        }

        if (tab.Snapshot is { } snapshot)
        {
            foreach (var r in snapshot.Refs) a.AddRef(r.FullName);
            if (snapshot.Head.Branch is { } branch) a.AddRef("refs/heads/" + branch);
            foreach (var remote in snapshot.Remotes)
            {
                a.AddText(remote.Name);
                a.AddUrl(remote.Url);
            }
            foreach (var stash in snapshot.Stashes)
            {
                a.AddText(stash.Message);
                a.AddText(stash.Author);
            }
            foreach (var entry in snapshot.Status.Entries)
            {
                AddPath(a, entry.Path);
                if (entry.OriginalPath is { } from) AddPath(a, from);
            }
            if (snapshot.Operation.Incoming.Length > 0) a.AddText(snapshot.Operation.Incoming);
        }

        // Only the commits read so far (the ones scrolled past): those are all the UI can show.
        foreach (var row in tab.History.Rows)
        {
            if (row.Info is not { } info) continue;
            a.AddText(info.Author.Name);
            a.AddText(info.Author.Email);
            a.AddText(info.Committer.Name);
            a.AddText(info.Committer.Email);
            a.AddText(info.Summary);
        }

        foreach (var file in tab.History.Details.Files.Concat(tab.Stash.Details.Files)) AddPath(a, file.Path);
        foreach (var diff in (ReadOnlySpan<DiffViewModel>)[tab.Diff, tab.History.Diff, tab.Stash.Diff])
        {
            if (diff.Diff is not { } d) continue;
            AddPath(a, d.Path);
            AddPath(a, d.OldPath);
        }
    }

    /// <summary>As the UI shows it (control characters escaped), which is what the dictionary is matched against.</summary>
    private static void AddPath(Anonymizer a, GitPath path) => a.AddPath(DisplayText.Path(path));
}
