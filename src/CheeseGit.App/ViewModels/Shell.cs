using System.Text;
using CheeseGit.Core.Git;

namespace CheeseGit.App.ViewModels;

/// <summary>What view models need from the window: alerts, dialogs and platform services. Tests fake it.</summary>
public interface IAppShell
{
    void ShowError(string message);

    /// <summary>
    /// Shows an info alert. It replaces the previous transient one and goes after a while;
    /// <paramref name="sticky"/> keeps it until dismissed (an offer the user should decide on).
    /// </summary>
    void ShowInfo(string message, string? actionLabel = null, Func<Task>? action = null, bool sticky = false, Action? onDismissed = null);

    /// <summary>Shows an in-window confirmation and returns whether the user confirmed.</summary>
    Task<bool> ConfirmAsync(string message, string confirmLabel, bool destructive = true);

    /// <summary>Shows an in-window dialog and returns its result when it closes.</summary>
    Task<bool> ShowDialogAsync(DialogViewModel dialog);

    Task<string?> PickFolderAsync(string title);

    Task SetClipboardTextAsync(string text);

    Task SaveTextFileAsync(string title, string suggestedName, string text);
}

/// <summary>Turns git data into text that is safe to show.</summary>
public static class DisplayText
{
    /// <summary>
    /// Control characters in file names (a newline, a tab, an escape) would
    /// break the single-line layout or render as nothing, so they are shown as
    /// visible symbols. Invalid UTF-8 is already U+FFFD from <see cref="GitPath.ToString"/>.
    /// </summary>
    public static string Escape(string text)
    {
        if (!text.Any(char.IsControl)) return text;
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            sb.Append(c switch
            {
                '\n' => '⏎',
                '\t' => '⇥',
                '\r' => '␍',
                < ' ' => (char)(0x2400 + c), // U+2400 "Control Pictures"
                '\x7f' => '␡',
                _ when char.IsControl(c) => '�',
                _ => c,
            });
        }
        return sb.ToString();
    }

    public static string Path(GitPath path) => Escape(path.ToString());

    public static string Name(GitPath path) => Escape(path.Name);

    /// <summary>Compact counts like the old app's <c>formatCount</c>: 950, 2.3K, 15K, 1.2M.</summary>
    public static string Count(int n) => n switch
    {
        < 1000 => n.ToString(System.Globalization.CultureInfo.InvariantCulture),
        < 10_000 => (n / 1000.0).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "K",
        < 1_000_000 => (n / 1000).ToString(System.Globalization.CultureInfo.InvariantCulture) + "K",
        _ => (n / 1_000_000.0).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "M",
    };
}
