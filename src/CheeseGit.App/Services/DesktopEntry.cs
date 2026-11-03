using System.Text;

namespace CheeseGit.App.Services;

public enum DesktopEntryStatus
{
    /// <summary>Not an AppImage: packages install their own entry, and other systems have none.</summary>
    NotApplicable,
    Missing,
    Current,

    /// <summary>The entry starts another file (the AppImage moved, or it is the old app's).</summary>
    Stale,
}

/// <summary>
/// Puts an AppImage into the application launcher (the old app's <c>desktop_entry</c> module): a
/// <c>.desktop</c> file and the icon under <c>$XDG_DATA_HOME</c>. AppImages have no installer, so without
/// this CheeseGit only starts from a file manager.
/// </summary>
public static class DesktopEntry
{
    /// <summary>The app id of the Linux packages (Flatpak, metainfo, desktop files) and the windows' WM_CLASS / app id.</summary>
    public const string AppId = "io.github.LPeter1997.CheeseGit";

    /// <summary>The old app's file name, so registering replaces its entry instead of adding a second one.</summary>
    private const string FileName = "cheesegit.desktop";

    /// <summary>Velopack's (and every) AppImage runtime sets <c>APPIMAGE</c> to the image file's path.</summary>
    public static string? AppImagePath =>
        OperatingSystem.IsLinux() && Environment.GetEnvironmentVariable("APPIMAGE") is { Length: > 0 } path ? path : null;

    public static string DataHome =>
        Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } dir && Path.IsPathRooted(dir)
            ? dir
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");

    public static string DesktopFilePath(string dataHome) => Path.Combine(dataHome, "applications", FileName);

    public static DesktopEntryStatus Check(string? appImage, string dataHome)
    {
        if (appImage is null) return DesktopEntryStatus.NotApplicable;
        string text;
        try
        {
            text = File.ReadAllText(DesktopFilePath(dataHome));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return DesktopEntryStatus.Missing;
        }
        var exec = text.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("Exec=", StringComparison.Ordinal));
        return exec == "Exec=" + ExecLine(appImage) ? DesktopEntryStatus.Current : DesktopEntryStatus.Stale;
    }

    /// <summary>Writes the icon and the entry. Throws on I/O errors; the caller reports them.</summary>
    public static void Register(string appImage, string dataHome, byte[] iconPng)
    {
        var iconDir = Path.Combine(dataHome, "icons", "hicolor", "512x512", "apps");
        Directory.CreateDirectory(iconDir);
        var icon = Path.Combine(iconDir, AppId + ".png");
        File.WriteAllBytes(icon, iconPng);

        var file = DesktopFilePath(dataHome);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var temp = file + ".tmp";
        File.WriteAllText(temp, Contents(ExecLine(appImage), icon));
        File.Move(temp, file, overwrite: true);
    }

    internal static string Contents(string exec, string icon) => $"""
        [Desktop Entry]
        Type=Application
        Name=CheeseGit
        GenericName=Git client
        Comment=A git client for every desktop
        Exec={exec}
        Icon={icon}
        Terminal=false
        Categories=Development;RevisionControl;
        Keywords=git;version control;commit;branch;
        StartupWMClass={AppId}

        """;

    /// <summary>The program quoted by the Desktop Entry rules (spaces, quotes, $ and backticks), then the folder arguments.</summary>
    internal static string ExecLine(string program)
    {
        var needsQuotes = program.Any(c => c is ' ' or '\t' or '"' or '\'' or '\\' or '>' or '<' or '~' or '|' or '&' or ';' or '$' or '*' or '?' or '#' or '(' or ')' or '`');
        var escaped = new StringBuilder();
        foreach (var c in program)
        {
            if (needsQuotes && c is '"' or '`' or '$' or '\\') escaped.Append('\\');
            // A literal % must be doubled, in or out of quotes (field codes).
            escaped.Append(c == '%' ? "%%" : c.ToString());
        }
        // Inside a desktop file's string value, a backslash is itself escaped once more.
        var value = needsQuotes ? "\"" + escaped.ToString().Replace("\\", "\\\\") + "\"" : escaped.ToString();
        return value + " %F";
    }
}
