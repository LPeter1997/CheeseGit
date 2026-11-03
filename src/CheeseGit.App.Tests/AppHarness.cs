using System.Diagnostics;
using Avalonia;
using Avalonia.Threading;
using CheeseGit.App.Services;
using CheeseGit.App.ViewModels;
using CheeseGit.Core.Git;
using CheeseGit.Core.Tests;

namespace CheeseGit.App.Tests;

/// <summary>
/// The app's view models over a real repository, as the window would drive
/// them. Session events reach the view models through the dispatcher, so
/// tests wait with <see cref="Until"/>, which keeps the dispatcher running.
/// </summary>
public sealed class AppHarness : IDisposable
{
    private AppHarness(MainViewModel main, RepoTabViewModel tab)
    {
        this.Main = main;
        this.Tab = tab;
    }

    public MainViewModel Main { get; }

    public RepoTabViewModel Tab { get; }

    public StagingViewModel Staging => this.Tab.Staging;

    public DiffViewModel Diff => this.Tab.Diff;

    public static MainViewModel CreateMain(AppSettings? settings = null) =>
        new(SettingsStore.InMemory(settings), ((App)Application.Current!).Themes, new GitRunner(), "test");

    public static async Task<AppHarness> OpenAsync(TempRepo repo, AppSettings? settings = null)
    {
        var main = CreateMain(settings);
        await main.OpenRepositoryAsync(repo.Path);
        var tab = Assert.Single(main.Tabs);
        Assert.Null(tab.OpenError);
        var harness = new AppHarness(main, tab);
        await Until(() => tab.Staging.Loaded, "the staging lists to load");
        return harness;
    }

    public FileRow Unstaged(string path) =>
        this.Staging.Unstaged.All.FirstOrDefault(r => r.DisplayPath == path) ?? throw new Xunit.Sdk.XunitException($"{path} is not in the unstaged list");

    public FileRow StagedRow(string path) =>
        this.Staging.Staged.All.FirstOrDefault(r => r.DisplayPath == path) ?? throw new Xunit.Sdk.XunitException($"{path} is not in the staged list");

    public List<string> UnstagedPaths => this.Staging.Unstaged.All.Select(r => r.DisplayPath).ToList();

    public List<string> StagedPaths => this.Staging.Staged.All.Select(r => r.DisplayPath).ToList();

    /// <summary>Selects <paramref name="row"/> like a click and waits for its diff.</summary>
    public async Task ShowAsync(FileRow row)
    {
        var list = row.Area == Core.Repository.DiffArea.Staged ? this.Staging.Staged : this.Staging.Unstaged;
        list.Click(row, ctrl: false, shift: false);
        await Until(() => this.Diff.Diff is { } d && d.Path == row.Path, $"the diff of {row.DisplayPath}");
    }

    public static async Task Until(Func<bool> condition, string what, int timeoutMs = 15000)
    {
        var sw = Stopwatch.StartNew();
        while (!condition())
        {
            if (sw.ElapsedMilliseconds > timeoutMs) throw new TimeoutException($"Timed out waiting for {what}.");
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
    }

    /// <summary>The git, helper and shell processes alive now, for timeout messages (other tests' git calls included).</summary>
    public static string GitProcesses() => string.Join(", ", Process.GetProcesses()
        .Where(p => (p.ProcessName.Contains("git", StringComparison.OrdinalIgnoreCase) && !p.ProcessName.StartsWith("CheeseGit", StringComparison.Ordinal)) || p.ProcessName.Contains("credential", StringComparison.OrdinalIgnoreCase) || p.ProcessName is "sh" or "bash" or "ssh")
        .Select(p => $"{p.ProcessName} ({p.Id})"));

    public void Dispose() => this.Main.Dispose();
}
