using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace CheeseGit.Core.Git;

/// <summary>
/// The label of a <see cref="GitOperation"/> ("Stage notes.txt"). Written as an
/// interpolated string, it remembers what was filled in: those are the user's
/// names (files, branches, remotes), which a problem report must anonymize
/// even after they are gone from the repository, while the label text around
/// them stays readable. Numbers are not private; mark another public value
/// with the <c>public</c> format: <c>$"Abort {verb:public}"</c>.
/// </summary>
[InterpolatedStringHandler]
public sealed class OperationLabel
{
    private const string PublicFormat = "public";

    private readonly StringBuilder? builder;
    private List<string>? subjects;
    private string? text;

    public OperationLabel(int literalLength, int formattedCount) => this.builder = new StringBuilder(literalLength + 16 * formattedCount);

    private OperationLabel(string text) => this.text = text;

    public static implicit operator OperationLabel(string text) => new(text);

    public string Text => this.text ??= this.builder?.ToString() ?? "";

    /// <summary>The filled-in values that are private (see the class summary).</summary>
    public IReadOnlyList<string> Subjects => this.subjects ?? [];

    public void AppendLiteral(string value) => this.builder!.Append(value);

    public void AppendFormatted<T>(T value) => this.AppendFormatted(value, null);

    public void AppendFormatted<T>(T value, string? format)
    {
        var isPublic = format == PublicFormat || typeof(T).IsPrimitive;
        if (format == PublicFormat) format = null;
        var s = value is IFormattable formattable ? formattable.ToString(format, CultureInfo.InvariantCulture) : value?.ToString() ?? "";
        this.builder!.Append(s);
        if (!isPublic && s.Length > 0) (this.subjects ??= []).Add(s);
    }

    public override string ToString() => this.Text;
}
