using System.Text;

namespace CheeseGit.Fixtures;

/// <summary>File names that broke the old app's text-based git parsing (quoting, splitting on whitespace or newlines, option injection).</summary>
public static class HostileNames
{
    private static readonly string[] Portable =
    [
        "plain.txt",
        "with space.txt",
        " leading space.txt",
        "trailing space .txt",
        "ünïcödé ✓.txt",
        "日本語/ファイル.txt",
        "emoji 🧀.txt",
        "-starts-with-dash.txt",
        "--option-like.txt",
        "apostrophe's.txt",
        "percent %s %n.txt",
        "dir with space/nested file.txt",
        "deep/a/b/c/d/e/f/g/h/i/j/k/l/m/n/o/p/q/r/s/t/u/v/w/x/y/z/long_file_name_to_stress_display_and_quoting.txt",
        "#hash.txt",
        "semicolon;and&amp.txt",
        "brackets[1](2){3}.txt",
        "UPPER.TXT",
    ];

    // Not valid on Windows (":" included, so no pathspec magic there); newlines and invalid UTF-8 are rejected by macOS too.
    private static readonly string[] Posix = [":(top)magic.txt", "quote\"double.txt", "tab\there.txt", "glob*.txt", "question?.txt", "back\\slash.txt", "pipe|and<angle>.txt"];

    /// <summary>Names valid on the current OS, as raw bytes.</summary>
    public static IReadOnlyList<byte[]> ForThisOs()
    {
        var names = Portable.Select(Encoding.UTF8.GetBytes).ToList();
        if (!OperatingSystem.IsWindows()) names.AddRange(Posix.Select(Encoding.UTF8.GetBytes));
        if (OperatingSystem.IsLinux())
        {
            names.Add("new\nline.txt"u8.ToArray());
            names.Add([.. "latin1-caf"u8, 0xE9, .. ".txt"u8]);
            names.Add([.. "invalid-utf8-"u8, 0xFF, 0xFE, .. ".txt"u8]);
        }
        return names;
    }
}
