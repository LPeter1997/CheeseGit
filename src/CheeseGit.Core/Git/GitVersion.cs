using System.Text;

namespace CheeseGit.Core.Git;

/// <summary>The installed git version, and the features that depend on it.</summary>
public sealed record GitVersion(int Major, int Minor, int Patch, string Raw) : IComparable<GitVersion>
{
    /// <summary>Ubuntu 22.04's git. Older versions lack options we rely on (e.g. <c>--pathspec-file-nul</c> everywhere).</summary>
    public static readonly GitVersion Minimum = new(2, 34, 0, "2.34.0");

    /// <summary><c>git stash push --staged</c> (2.35).</summary>
    public bool SupportsStashStaged => this.AtLeast(2, 35);

    /// <summary><c>git merge-tree --write-tree</c> for conflict prediction (2.38).</summary>
    public bool SupportsMergeTreeWriteTree => this.AtLeast(2, 38);

    public bool AtLeast(int major, int minor, int patch = 0) =>
        (this.Major, this.Minor, this.Patch).CompareTo((major, minor, patch)) >= 0;

    public int CompareTo(GitVersion? other) =>
        other is null ? 1 : (this.Major, this.Minor, this.Patch).CompareTo((other.Major, other.Minor, other.Patch));

    /// <summary>Parses <c>git version 2.43.0</c>, <c>git version 2.39.3 (Apple Git-146)</c>, <c>git version 2.45.1.windows.1</c>.</summary>
    public static GitVersion? TryParse(string output)
    {
        var text = output.Trim();
        const string prefix = "git version ";
        if (!text.StartsWith(prefix, StringComparison.Ordinal)) return null;
        var parts = text[prefix.Length..].Split(' ')[0].Split('.');
        if (parts.Length < 2) return null;
        if (!int.TryParse(parts[0], out var major) || !int.TryParse(parts[1], out var minor)) return null;
        var patch = parts.Length > 2 && int.TryParse(parts[2], out var p) ? p : 0;
        return new GitVersion(major, minor, patch, text[prefix.Length..]);
    }

    /// <summary>Runs <c>git version</c>.</summary>
    /// <param name="directory">Where to run it; the command log names the repository after it (default: the current directory).</param>
    public static async Task<GitVersion> DetectAsync(GitRunner git, CancellationToken ct = default, string? directory = null)
    {
        var result = await git.RunCheckedAsync("Detect git version", directory ?? Directory.GetCurrentDirectory(), ["version"], ct: ct);
        return TryParse(Encoding.UTF8.GetString(result.Stdout))
            ?? throw new GitOperationException(GitErrorCode.UnexpectedOutput, $"Unrecognized git version output: {result.StdoutText.Trim()}");
    }

    public override string ToString() => this.Raw;
}
