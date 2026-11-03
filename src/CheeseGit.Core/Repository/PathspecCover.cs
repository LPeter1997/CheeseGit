using CheeseGit.Core.Git;

namespace CheeseGit.Core.Repository;

/// <summary>
/// Shrinks a long path list to the directories it fully covers. git matches
/// every pathspec against every index entry, so "stage all" of 15k changes in
/// a 100k-file tree took 37 s for <c>add</c> and 31 s for <c>reset</c>; with
/// the whole tree as one pathspec both take under half a second.
/// </summary>
internal static class PathspecCover
{
    /// <summary>Below this, the list is passed as is: covering needs a status read, which costs more than it saves.</summary>
    public const int Threshold = 100;

    /// <summary>
    /// Replaces the paths under a directory with the directory itself when
    /// every path of <paramref name="changed"/> under it is selected, so the
    /// command touches exactly what the full list would have: nothing else
    /// under that directory differs from HEAD, the index or the working tree.
    /// <paramref name="changed"/> must hold every changed path (all status
    /// entries, with rename sources). The whole tree becomes ".".
    /// </summary>
    public static List<GitPath> Cover(IReadOnlyCollection<GitPath> selected, IEnumerable<GitPath> changed)
    {
        var selectedSet = selected.ToHashSet();
        var total = new Dictionary<GitPath, int>();
        var covered = new Dictionary<GitPath, int>();
        foreach (var path in changed.Distinct())
        {
            var isSelected = selectedSet.Contains(path);
            foreach (var dir in Directories(path))
            {
                total[dir] = total.GetValueOrDefault(dir) + 1;
                if (isSelected) covered[dir] = covered.GetValueOrDefault(dir) + 1;
            }
        }

        var result = new List<GitPath>();
        var emitted = new HashSet<GitPath>();
        foreach (var path in selectedSet)
        {
            // The outermost fully covered directory, else the path itself (also for paths status no longer lists).
            var pathspec = path;
            foreach (var dir in Directories(path))
            {
                if (total.TryGetValue(dir, out var t) && covered.GetValueOrDefault(dir) == t)
                {
                    pathspec = dir;
                    break;
                }
            }
            if (emitted.Add(pathspec)) result.Add(pathspec);
        }
        return result;
    }

    /// <summary>The directories containing <paramref name="path"/>, outermost first: ".", "a", "a/b".</summary>
    private static IEnumerable<GitPath> Directories(GitPath path)
    {
        yield return Root;
        var bytes = path.Bytes.ToArray();
        for (var i = 0; i < bytes.Length; i++)
        {
            if (bytes[i] == (byte)'/') yield return GitPath.FromBytes(bytes.AsSpan(0, i));
        }
    }

    private static readonly GitPath Root = GitPath.FromString(".");
}
