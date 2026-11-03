using CheeseGit.Core.Git;

namespace CheeseGit.Core.Tests;

/// <summary>git's progress meter (<c>--progress</c>) for fetch, push and pull: parsing and delivery.</summary>
public class ProgressTests
{
    /// <summary>Collects reports in order, on the reporting thread (<see cref="Progress{T}"/> would reorder them).</summary>
    private sealed class Recorder : IProgress<GitProgress>
    {
        private readonly List<GitProgress> reports = [];

        public void Report(GitProgress value)
        {
            lock (this.reports) this.reports.Add(value);
        }

        public List<GitProgress> Reports
        {
            get
            {
                lock (this.reports) return [.. this.reports];
            }
        }
    }

    [Theory]
    [InlineData("Receiving objects:  45% (450/1000), 1.20 MiB | 600.00 KiB/s", "Receiving objects", 45, 450L, 1000L, false)]
    [InlineData("Writing objects: 100% (90/90), 1.72 MiB | 25.74 MiB/s, done.", "Writing objects", 100, 90L, 90L, false)]
    [InlineData("remote: Counting objects:   7% (7/90)        ", "Counting objects", 7, 7L, 90L, true)]
    [InlineData("remote: Enumerating objects: 90, done.        ", "Enumerating objects", null, 90L, null, true)]
    [InlineData("Enumerating objects: 5", "Enumerating objects", null, 5L, null, false)]
    public void Parses_meters(string line, string phase, int? percent, long? done, long? total, bool remote)
    {
        var p = GitProgress.Parse(line);
        Assert.Equal(phase, p.Phase);
        Assert.Equal(percent, p.Percent);
        Assert.Equal(done, p.Done);
        Assert.Equal(total, p.Total);
        Assert.Equal(remote, p.Remote);
    }

    [Theory]
    [InlineData("From ../remote")]
    [InlineData(" * [new branch]      main       -> origin/main")]
    [InlineData("Total 90 (delta 27), reused 0 (delta 0), pack-reused 0 (from 0)")]
    [InlineData("remote: hook says: 3 things")]
    [InlineData("To ../remote.git")]
    public void Other_lines_are_not_meters(string line)
    {
        var p = GitProgress.Parse(line);
        Assert.Null(p.Done);
        Assert.Null(p.Percent);
    }

    /// <summary>A remote with enough objects that every phase draws its meter more than once.</summary>
    private static (TempRepo Origin, TempRepo Clone) RemoteWithObjects(int files = 30)
    {
        var origin = TempRepo.InitBare();
        var clone = TempRepo.CloneOf(origin);
        var random = new Random(7);
        for (var i = 0; i < files; i++)
        {
            var content = new byte[4096];
            random.NextBytes(content);
            clone.Write($"f{i}.bin", content);
        }
        clone.CommitAll("objects");
        return (origin, clone);
    }

    [Fact]
    public async Task Push_reports_its_phases_and_logs_only_their_last_state()
    {
        var (origin, clone) = RemoteWithObjects();
        using var _ = origin;
        using var __ = clone;
        var logged = new List<GitInvocation>();
        clone.Git.Invoked += i =>
        {
            lock (logged) logged.Add(i);
        };
        using var git = await clone.OpenAsync();
        var recorder = new Recorder();
        await git.PublishAsync("origin", recorder);

        var writing = recorder.Reports.Where(p => p.Phase == "Writing objects" && !p.Remote).ToList();
        Assert.True(writing.Count > 1, $"several Writing objects meters (got {writing.Count})");
        Assert.Equal(100, writing[^1].Percent);
        Assert.Equal(writing.Select(p => p.Percent).Order(), writing.Select(p => p.Percent));

        GitInvocation push;
        lock (logged) push = logged.Single(i => i.Args.Contains("push"));
        var stderr = System.Text.Encoding.UTF8.GetString(push.Stderr);
        Assert.DoesNotContain('\r', stderr);
        Assert.Single(stderr.Split('\n'), l => l.StartsWith("Writing objects:", StringComparison.Ordinal));
        Assert.Contains("Writing objects: 100%", stderr);
    }

    [Fact]
    public async Task Fetch_and_pull_report_the_objects_they_receive()
    {
        var (origin, clone) = RemoteWithObjects();
        using var _ = origin;
        using var __ = clone;
        clone.Raw("push", "-q", "-u", "origin", "HEAD");
        using var other = TempRepo.CloneOf(origin);
        // Below fetch.unpackLimit (100 objects) git unpacks loose objects, and unpack-objects draws
        // its meter only on a terminal, whatever --progress says: only the server's meters come.
        other.Raw("config", "fetch.unpackLimit", "1");
        using var git = await other.OpenAsync();
        var branch = other.CurrentBranch();

        void PushRound(int round)
        {
            var random = new Random(round);
            for (var i = 0; i < 20; i++)
            {
                var content = new byte[4096];
                random.NextBytes(content);
                clone.Write($"g{round}-{i}.bin", content);
            }
            clone.CommitAll($"round {round}");
            clone.Raw("push", "-q", "origin", "HEAD");
        }

        // The first round arrives by fetch, the second by pull.
        PushRound(0);
        var fetched = new Recorder();
        await git.FetchAsync("origin", fetched);
        Assert.Contains(fetched.Reports, p => p.Phase == "Receiving objects" && p.Percent == 100 && !p.Remote);

        PushRound(1);
        var pulled = new Recorder();
        await git.PullAsync("origin", pulled);
        // A merge commit under merge.ff=false (the merge profile), else a fast-forward.
        other.Raw("merge-base", "--is-ancestor", clone.Head(), "HEAD");
        Assert.Equal(branch, other.CurrentBranch());
        Assert.Contains(pulled.Reports, p => p.Phase == "Receiving objects" && p.Percent == 100 && !p.Remote);
    }

    [Fact]
    public async Task Without_a_receiver_git_is_not_asked_for_progress()
    {
        var (origin, clone) = RemoteWithObjects(3);
        using var _ = origin;
        using var __ = clone;
        var logged = new List<GitInvocation>();
        clone.Git.Invoked += i =>
        {
            lock (logged) logged.Add(i);
        };
        using var git = await clone.OpenAsync();
        await git.PublishAsync("origin");
        lock (logged) Assert.DoesNotContain("--progress", logged.Single(i => i.Args.Contains("push")).Args);
    }
}
