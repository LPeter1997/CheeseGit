using CheeseGit.Fixtures;

namespace CheeseGit.Core.Tests;

/// <summary>
/// Keeps generated fixtures across test runs. Only for tests that never
/// modify the repository: the 100k-commit history takes ~14 s to import
/// (fast-import costs ~100 µs per commit), while reading it takes ~1 s.
/// </summary>
internal static class FixtureCache
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "cheesegit-fixture-cache");

    public static async Task<Fixture> GetReadOnlyAsync(FixtureKind kind, FixtureOptions options)
    {
        var key = $"{kind}-{options.LargeHistoryCommits}-{options.ManyRefsBranches}-{options.LargeWorktreeFiles}-v{FixtureGenerator.Version}";
        var directory = Path.Combine(Root, key);
        var fixture = new Fixture(kind, Path.Combine(directory, "repo"));
        if (Directory.Exists(directory)) return fixture;

        // Generate next to the target and move it into place, so concurrent
        // runs and interrupted generation never leave a half-built fixture.
        var staging = Path.Combine(Root, $"{key}.tmp-{Guid.NewGuid():N}");
        await FixtureGenerator.CreateAsync(kind, staging, options);
        try
        {
            Directory.Move(staging, directory);
        }
        catch (IOException) when (Directory.Exists(directory))
        {
            Directory.Delete(staging, recursive: true);
        }
        return fixture;
    }
}
