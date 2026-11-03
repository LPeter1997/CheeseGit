using System.Text;
using CheeseGit.Core.Git;

namespace CheeseGit.Core.Graph;

/// <summary>Loads commit topology from git.</summary>
public static class DagLoader
{
    /// <summary>
    /// Loads every commit reachable from <paramref name="revisions"/> (full ref
    /// names or object ids), in topological order, optionally capped at
    /// <paramref name="maxCount"/> commits. Revisions go through stdin so
    /// thousands of refs never hit command-line length limits.
    /// </summary>
    public static async Task<CommitDag> LoadAsync(
        GitRunner git,
        string repo,
        IReadOnlyCollection<string> revisions,
        int? maxCount = null,
        CancellationToken ct = default)
    {
        var builder = new CommitDag.Builder(1 << 12);
        if (revisions.Count == 0) return builder.Build();

        var stdin = Encoding.UTF8.GetBytes(string.Join('\n', revisions) + "\n");
        string? error = null;
        List<string> args = ["rev-list", "--topo-order", "--parents", "--timestamp"];
        if (maxCount is { } max) args.Add($"--max-count={max}");
        args.Add("--stdin");

        var result = await git.StreamRecordsAsync(
            repo,
            args,
            (byte)'\n',
            record =>
            {
                if (record.IsEmpty || error is not null) return;
                if (!TryParseRecord(record, builder)) error = Encoding.UTF8.GetString(record);
            },
            stdin: stdin,
            ct: ct);

        result.EnsureSuccess("Load history");
        if (error is not null) throw new GitOperationException(GitErrorCode.UnexpectedOutput, $"Unexpected rev-list output: {error}");
        return builder.Build();
    }

    // Record: "<committer unix time> <oid> <parent oid> <parent oid>...".
    // rev-list reads all of this from the commit-graph file when present;
    // git log with a %ct format parses every commit object instead
    // (1,281 ms vs 248 ms on git/git, measured in Phase 0).
    private static bool TryParseRecord(ReadOnlySpan<byte> record, CommitDag.Builder builder)
    {
        var space = record.IndexOf((byte)' ');
        if (space < 0 || !System.Buffers.Text.Utf8Parser.TryParse(record[..space], out long time, out _)) return false;
        record = record[(space + 1)..];

        space = record.IndexOf((byte)' ');
        if (!Oid.TryParseHex(space < 0 ? record : record[..space], out var oid)) return false;
        builder.BeginCommit(oid, time);
        if (space < 0) return true;

        record = record[(space + 1)..];
        while (!record.IsEmpty)
        {
            space = record.IndexOf((byte)' ');
            if (!Oid.TryParseHex(space < 0 ? record : record[..space], out var parent)) return false;
            builder.AddParent(parent);
            record = space < 0 ? [] : record[(space + 1)..];
        }
        return true;
    }
}
