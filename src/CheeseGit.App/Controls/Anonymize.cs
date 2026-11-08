using Avalonia;

namespace CheeseGit.App.Controls;

/// <summary>How a problem-report screenshot treats the text under a control (see <see cref="Services.ReportCapture"/>).</summary>
public enum AnonymizeMode
{
    /// <summary>The app's own text with names in it ("Stage notes.txt"): only known private names are replaced.</summary>
    Tokens,

    /// <summary>Free text the user wrote (commit messages): every word is scrambled.</summary>
    Scramble,
}

/// <summary>
/// The attached <c>c:Anonymize.Mode</c>. Inherited, so marking a list or a
/// panel covers every text inside it, templates included.
/// </summary>
public static class Anonymize
{
    public static readonly AttachedProperty<AnonymizeMode> ModeProperty =
        AvaloniaProperty.RegisterAttached<AvaloniaObject, AnonymizeMode>("Mode", typeof(Anonymize), AnonymizeMode.Tokens, inherits: true);

    public static AnonymizeMode GetMode(AvaloniaObject element) => element.GetValue(ModeProperty);

    public static void SetMode(AvaloniaObject element, AnonymizeMode value) => element.SetValue(ModeProperty, value);
}
