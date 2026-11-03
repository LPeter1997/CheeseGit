using Avalonia.Headless.XUnit;
using CheeseGit.App.ViewModels;
using CheeseGit.Core.Tests;

namespace CheeseGit.App.Tests;

/// <summary>Phase 4: the remote button and picker over real repositories and bare remotes.</summary>
public class RemoteTests
{
    private static TempRepo RepoWithCommit()
    {
        var repo = TempRepo.Init();
        repo.CommitFile("a.txt", "a\n", "first");
        return repo;
    }

    private static async Task<RemoteViewModel> RemoteAsync(AppHarness app, RemoteAction expected)
    {
        var remote = app.Tab.Remote;
        await AppHarness.Until(() => remote.Action == expected, $"the remote action to be {expected} (it is {remote.Action}: {remote.Label})");
        return remote;
    }

    [AvaloniaFact]
    public async Task Adding_the_first_remote_then_publish_push_fetch_and_pull()
    {
        using var bare = TempRepo.InitBare();
        using var repo = RepoWithCommit();
        using var app = await AppHarness.OpenAsync(repo);
        var remote = await RemoteAsync(app, RemoteAction.Add);
        Assert.Equal("Add remote", remote.Label);

        // Add: the first remote is named origin.
        var adding = remote.RunCommand.ExecuteAsync(null);
        var dialog = Assert.IsType<AddRemoteDialogViewModel>(app.Main.Dialog);
        Assert.Equal("origin", dialog.Name);
        dialog.Url = bare.Path;
        await dialog.AddCommand.ExecuteAsync(null);
        await adding;
        Assert.Null(app.Main.Dialog);

        await RemoteAsync(app, RemoteAction.Publish);
        Assert.Equal("origin", remote.Active);
        Assert.Equal("Publish to origin", remote.Label);
        await remote.RunCommand.ExecuteAsync(null);
        var branch = repo.CurrentBranch();
        Assert.Equal(repo.Head(), bare.Raw("rev-parse", branch).Trim());

        await RemoteAsync(app, RemoteAction.Fetch);
        Assert.Equal("Fetch origin", remote.Label);

        repo.CommitFile("b.txt", "b\n", "second");
        await RemoteAsync(app, RemoteAction.Push);
        Assert.Equal("Push 1 to origin", remote.Label);
        await remote.RunCommand.ExecuteAsync(null);
        Assert.Equal(repo.Head(), bare.Raw("rev-parse", branch).Trim());
        await RemoteAsync(app, RemoteAction.Fetch);

        // Someone else pushes; fetching shows it, pulling brings it in.
        using var other = TempRepo.CloneOf(bare);
        other.CommitFile("c.txt", "c\n", "theirs");
        other.Raw("push", "-q", "origin", "HEAD");
        await remote.RunCommand.ExecuteAsync(null);
        await RemoteAsync(app, RemoteAction.Pull);
        Assert.Equal("Pull 1 from origin", remote.Label);
        await remote.RunCommand.ExecuteAsync(null);
        repo.Raw("merge-base", "--is-ancestor", other.Head(), "HEAD");
        // A fast-forward leaves nothing to do; with merge.ff=false (the merge profile) the merge commit is to push.
        await AppHarness.Until(() => remote.Action is RemoteAction.Fetch or RemoteAction.Push, "the pull to show");
        Assert.Equal(repo.Head() == other.Head() ? RemoteAction.Fetch : RemoteAction.Push, remote.Action);
        Assert.Empty(app.Main.Alerts);
    }

    [AvaloniaFact]
    public async Task The_button_shows_gits_progress_while_it_runs()
    {
        using var bare = TempRepo.InitBare();
        using var repo = RepoWithCommit();
        var random = new Random(3);
        for (var i = 0; i < 40; i++)
        {
            var content = new byte[4096];
            random.NextBytes(content);
            repo.Write($"f{i}.bin", content);
        }
        repo.CommitAll("objects");
        repo.Raw("remote", "add", "origin", bare.Path);
        using var app = await AppHarness.OpenAsync(repo);
        var remote = await RemoteAsync(app, RemoteAction.Publish);

        var labels = new List<string>();
        var sawBar = false;
        remote.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(RemoteViewModel.Label)) labels.Add(remote.Label);
            if (e.PropertyName == nameof(RemoteViewModel.ProgressValue) && remote.HasProgressValue) sawBar = true;
        };
        await remote.RunCommand.ExecuteAsync(null);

        Assert.Equal("Publishing to origin…", labels[0]);
        Assert.Contains(labels, l => l.StartsWith("Writing objects ", StringComparison.Ordinal) && l.EndsWith('%'));
        Assert.True(sawBar, "the bar showed a percentage");
        Assert.Null(remote.Progress);
        Assert.False(remote.HasProgressValue);
        await RemoteAsync(app, RemoteAction.Fetch);
        Assert.Equal("Fetch origin", remote.Label);
    }

    [AvaloniaFact]
    public async Task Choosing_another_remote_compares_against_it_and_removing_falls_back()
    {
        using var origin = TempRepo.InitBare();
        using var mirror = TempRepo.InitBare();
        using var repo = RepoWithCommit();
        repo.Raw("remote", "add", "origin", origin.Path);
        repo.Raw("remote", "add", "mirror", mirror.Path);
        repo.Raw("push", "-q", "-u", "origin", "HEAD");
        using var app = await AppHarness.OpenAsync(repo);
        var remote = await RemoteAsync(app, RemoteAction.Fetch);
        Assert.Equal("origin", remote.Active);
        Assert.Equal(["mirror", "origin"], remote.Rows.Select(r => r.Name).Order(StringComparer.Ordinal));
        Assert.True(remote.Rows.Single(r => r.Name == "origin").IsActive);

        remote.Rows.Single(r => r.Name == "mirror").SelectCommand.Execute(null);
        Assert.Equal("mirror", remote.Active);
        await RemoteAsync(app, RemoteAction.Publish);
        Assert.Equal("Publish to mirror", remote.Label);

        // Pushing to a remote that is not the upstream compares against the branch of the same name there.
        repo.Raw("push", "-q", "mirror", "HEAD");
        repo.Raw("fetch", "-q", "mirror");
        await RemoteAsync(app, RemoteAction.Fetch);
        repo.CommitFile("b.txt", "b\n", "second");
        await RemoteAsync(app, RemoteAction.Push);
        Assert.Equal("Push 1 to mirror", remote.Label);

        // Removing asks in place first.
        var row = remote.Rows.Single(r => r.Name == "mirror");
        row.AskRemoveCommand.Execute(null);
        Assert.True(row.ConfirmingRemove);
        await row.RemoveCommand.ExecuteAsync(null);
        await AppHarness.Until(() => remote.Rows.Count == 1, "mirror to be removed");
        Assert.Equal("origin", remote.Active);
        Assert.Equal("", repo.Raw("remote").Trim().Replace("origin", ""));
    }

    [AvaloniaFact]
    public async Task A_failed_add_stays_in_the_dialog_and_adds_nothing()
    {
        using var repo = RepoWithCommit();
        using var app = await AppHarness.OpenAsync(repo);
        var remote = await RemoteAsync(app, RemoteAction.Add);

        var adding = remote.RunCommand.ExecuteAsync(null);
        var dialog = Assert.IsType<AddRemoteDialogViewModel>(app.Main.Dialog);
        dialog.Url = Path.Combine(repo.Path, "no-such-remote");
        await dialog.AddCommand.ExecuteAsync(null);
        Assert.Same(dialog, app.Main.Dialog);
        Assert.True(dialog.HasError);
        Assert.Equal("", repo.Raw("remote").Trim());

        dialog.CancelCommand.Execute(null);
        await adding;
        Assert.Equal(RemoteAction.Add, remote.Action);
    }

    [AvaloniaFact]
    public async Task A_hanging_fetch_is_cancelled_by_the_second_click()
    {
        using var repo = RepoWithCommit();
        // An ssh that never answers.
        repo.Raw("config", "core.sshCommand", "sh -c 'sleep 60' --");
        repo.Raw("remote", "add", "origin", "ssh://example.invalid/repo.git");
        repo.Raw("update-ref", "refs/remotes/origin/" + repo.CurrentBranch(), "HEAD");
        repo.Raw("branch", "-q", "--set-upstream-to=origin/" + repo.CurrentBranch());
        using var app = await AppHarness.OpenAsync(repo);
        var remote = await RemoteAsync(app, RemoteAction.Fetch);

        var fetching = remote.RunCommand.ExecuteAsync(null);
        await AppHarness.Until(() => remote.Busy, "the fetch to start");
        Assert.True(remote.CanAct);
        Assert.Equal("Click to cancel", remote.ToolTip);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await remote.RunCommand.ExecuteAsync(null);
        await fetching;
        Assert.False(remote.Busy);
        Assert.True(sw.ElapsedMilliseconds < 10_000, $"cancelling took {sw.ElapsedMilliseconds} ms");
        Assert.Empty(app.Main.Alerts);
    }

    [AvaloniaFact]
    public async Task Browsing_history_disables_syncing()
    {
        using var bare = TempRepo.InitBare();
        using var repo = RepoWithCommit();
        repo.CommitFile("b.txt", "b\n", "second");
        repo.Raw("remote", "add", "origin", bare.Path);
        repo.Raw("push", "-q", "-u", "origin", "HEAD");
        using var app = await AppHarness.OpenAsync(repo);
        var remote = await RemoteAsync(app, RemoteAction.Fetch);
        Assert.True(remote.CanAct);

        repo.Raw("switch", "-q", "--detach", "HEAD~1");
        await AppHarness.Until(() => app.Tab.BrowsingHistory, "history browsing");
        Assert.False(remote.CanAct);
        Assert.Equal("Sync disabled while viewing history", remote.ToolTip);
    }

    [Theory]
    [InlineData("git@github.com:user/repo.git", "github")]
    [InlineData("https://gitlab.com/user/repo.git", "gitlab")]
    [InlineData("ssh://git@codeberg.org:2222/user/repo.git", "codeberg")]
    [InlineData("https://user@git.sr.ht/~user/repo", "sr")]
    [InlineData("/srv/git/repo.git", "")]
    public void Remote_names_are_suggested_from_the_host(string url, string expected) =>
        Assert.Equal(expected, AddRemoteDialogViewModel.SuggestName(url));
}
