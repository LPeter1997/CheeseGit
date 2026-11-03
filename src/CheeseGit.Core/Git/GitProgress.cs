using System.Text.RegularExpressions;

namespace CheeseGit.Core.Git;

/// <summary>
/// One line of git's progress meter (<c>--progress</c>), for display only: success and state never
/// come from it. With <c>LC_ALL=C</c> the meter reads "Receiving objects:  45% (450/1000), 1.2 MiB |
/// 600 KiB/s" or, without a known total, "Enumerating objects: 5"; lines the server sends start
/// with "remote: ". A line that is not a meter still comes through, with no numbers, so the user
/// sees it ("Unpacking objects…", hook output).
/// </summary>
/// <param name="Phase">"Receiving objects"; the whole line when it is not a meter.</param>
/// <param name="Percent">0–100, or null when the phase has no known total.</param>
/// <param name="Remote">The server sent the line (it said "remote: ").</param>
public sealed partial record GitProgress(string Phase, int? Percent, long? Done, long? Total, string Line, bool Remote)
{
    public static GitProgress Parse(string line)
    {
        line = line.TrimEnd();
        var remote = line.StartsWith("remote: ", StringComparison.Ordinal);
        var body = remote ? line["remote: ".Length..].TrimStart() : line;
        var m = Meter().Match(body);
        if (!m.Success) return new GitProgress(body, null, null, null, line, remote);
        int? percent = m.Groups["pct"].Success ? Math.Clamp(int.Parse(m.Groups["pct"].ValueSpan), 0, 100) : null;
        long? done = long.TryParse(m.Groups["done"].ValueSpan, out var d) ? d : null;
        long? total = m.Groups["total"].Success && long.TryParse(m.Groups["total"].ValueSpan, out var t) ? t : null;
        return new GitProgress(m.Groups["phase"].Value, percent, done, total, line, remote);
    }

    [GeneratedRegex(@"^(?<phase>[A-Za-z][A-Za-z ]*):\s+(?:(?<pct>\d{1,3})%\s+\((?<done>\d+)/(?<total>\d+)\)|(?<done>\d+)(?=,|$))")]
    private static partial Regex Meter();
}
