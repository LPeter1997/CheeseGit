using System.Collections.ObjectModel;
using System.Text;
using Avalonia.Threading;
using CheeseGit.Core.Diagnostics;
using CheeseGit.Core.Git;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CheeseGit.App.ViewModels;

/// <summary>One git call inside an <see cref="OperationEntry"/>.</summary>
/// <param name="Args">The raw arguments, kept so a problem report can anonymize each one by what it is.</param>
public sealed record CommandCall(IReadOnlyList<string> Args, string CommandLine, int ExitCode, DateTimeOffset Started, TimeSpan Elapsed, string Stderr)
{
    /// <summary>A non-zero exit is often an answer (<c>rev-parse --verify</c>), not a failure; the operation says whether it failed.</summary>
    public bool NonZeroExit => this.ExitCode != 0;

    public bool HasStderr => this.Stderr.Length > 0;

    public string ElapsedText => FormatElapsed(this.Elapsed);

    public string ExitText => this.NonZeroExit ? $"exit {this.ExitCode}" : "";

    public string ToClipboardText() =>
        $"$ {this.CommandLine}\nexit code: {this.ExitCode} ({this.Elapsed.TotalMilliseconds:0} ms)" + (this.HasStderr ? $"\n--- stderr ---\n{this.Stderr}" : "");

    /// <summary>The command line, anonymized when <paramref name="anonymizer"/> is given.</summary>
    internal string CommandLineFor(Anonymizer? anonymizer) =>
        anonymizer is null ? this.CommandLine : CommandLogViewModel.FormatCommand(AnonymizeArgs(this.Args, anonymizer));

    internal string StderrFor(Anonymizer? anonymizer) => anonymizer is null ? this.Stderr : anonymizer.Scrub(this.Stderr);

    /// <summary>
    /// Each argument by what it is: refs after <c>--end-of-options</c>, paths after
    /// <c>--</c>, messages after <c>-m</c>/<c>--message</c>. Anything else is scrubbed
    /// (known names, URLs, ids) so options and subcommands stay readable.
    /// </summary>
    internal static IReadOnlyList<string> AnonymizeArgs(IReadOnlyList<string> args, Anonymizer anonymizer)
    {
        var result = new List<string>(args.Count);
        var (refs, paths, message) = (false, false, false);
        foreach (var arg in args)
        {
            if (message)
            {
                // "-m 1" picks a merge parent; a number there is not a message.
                result.Add(int.TryParse(arg, out _) ? arg : anonymizer.Text(arg));
                message = false;
                continue;
            }
            if (paths) result.Add(AnonymizePathspec(arg, anonymizer));
            else if (refs) result.Add(arg == "--" ? arg : anonymizer.Ref(arg));
            else if (arg.StartsWith("--message=", StringComparison.Ordinal)) result.Add("--message=" + anonymizer.Text(arg["--message=".Length..]));
            else result.Add(anonymizer.Scrub(arg));
            if (arg == "--") paths = true;
            else if (arg == "--end-of-options" && !refs) refs = true;
            else if (!refs && arg is "-m" or "--message") message = true;
        }
        return result;
    }

    /// <summary>A path, keeping pathspec magic (<c>:(glob)</c>, <c>:(literal)</c>): that is git's, not the user's.</summary>
    internal static string AnonymizePathspec(string arg, Anonymizer anonymizer)
    {
        var close = arg.StartsWith(":(", StringComparison.Ordinal) ? arg.IndexOf(')') : -1;
        return close < 0 ? anonymizer.Path(arg) : arg[..(close + 1)] + anonymizer.Path(arg[(close + 1)..]);
    }

    internal static string FormatElapsed(TimeSpan t) => t.TotalSeconds >= 10 ? $"{t.TotalSeconds:0} s" : t.TotalSeconds >= 1 ? $"{t.TotalSeconds:0.0} s" : $"{t.TotalMilliseconds:0} ms";
}

/// <summary>
/// One row of the command log: a user action (or a run of background
/// refreshes) with the git calls it made. Design agreed with the user on
/// 2026-10-31: grouped by operation, background work collapsed, errors stand out.
/// </summary>
public sealed partial class OperationEntry(long id, string label, string repository, bool background, DateTimeOffset started) : ObservableObject
{
    public long Id { get; private set; } = id;

    public string Label { get; } = label;

    /// <summary>Repository folder name, shown when several repositories are open.</summary>
    public string Repository { get; } = repository;

    public bool Background { get; } = background;

    public ObservableCollection<CommandCall> Calls { get; } = [];

    /// <summary>The private names in the label (<see cref="GitOperation.Subjects"/>), for problem reports.</summary>
    internal IReadOnlyList<string> Subjects { get; init; } = [];

    /// <summary>Full path of the repository, for problem reports (<see cref="Repository"/> is only its name).</summary>
    internal string WorkingDirectory { get; init; } = "";

    [ObservableProperty]
    public partial DateTimeOffset Started { get; private set; } = started;

    /// <summary>How many background runs this row stands for ("Refresh ×14").</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title))]
    public partial int Count { get; private set; } = 1;

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Succeeded), nameof(Failed))]
    public partial string? Error { get; private set; }

    public bool Failed => this.Error is not null;

    [ObservableProperty]
    public partial string Summary { get; private set; } = "";

    public bool Succeeded => !this.Failed;

    public string Title => this.Count > 1 ? $"{this.Label} ×{this.Count}" : this.Label;

    public string TimeText => this.Started.ToLocalTime().ToString("HH:mm:ss");

    internal void Add(CommandCall call)
    {
        this.Calls.Add(call);
        this.UpdateSummary();
    }

    internal void MarkFailed(string message) => this.Error = DisplayText.Escape(message.Trim());

    /// <summary>Folds another background run into this row; only the latest run's calls are kept.</summary>
    internal void Repeat(long id, DateTimeOffset started)
    {
        this.Id = id;
        this.Started = started;
        this.OnPropertyChanged(nameof(this.TimeText));
        this.Count++;
        this.Calls.Clear();
    }

    private void UpdateSummary()
    {
        if (this.Calls.Count == 0) return;
        var calls = this.Calls.Count == 1 ? "1 call" : $"{this.Calls.Count} calls";
        var end = this.Calls.Max(c => c.Started + c.Elapsed);
        this.Summary = $"{calls} · {CommandCall.FormatElapsed(end - this.Started)}" + (this.Background ? " · background" : "");
    }

    /// <summary>The row as text; with <paramref name="anonymizer"/>, every private part goes through it (problem reports).</summary>
    public string ToExportText(Anonymizer? anonymizer = null)
    {
        string Scrub(string text) => anonymizer?.Scrub(text) ?? text;
        var sb = new StringBuilder();
        sb.Append($"[{this.Started.ToLocalTime():yyyy-MM-dd HH:mm:ss}] {Scrub(this.Title)} ({Scrub(this.Repository)}){(this.Failed ? " FAILED" : "")}\n");
        if (this.Error is { } error) sb.Append("  ").Append(Scrub(error)).Append('\n');
        foreach (var call in this.Calls)
        {
            sb.Append($"  $ {call.CommandLineFor(anonymizer)}\n    exit {call.ExitCode}, {call.Elapsed.TotalMilliseconds:0} ms\n");
            if (call.HasStderr) sb.Append("    ").Append(call.StderrFor(anonymizer).Replace("\n", "\n    ")).Append('\n');
        }
        return sb.ToString();
    }
}

/// <summary>
/// Collects every <see cref="GitInvocation"/> of the shared <see cref="GitRunner"/>
/// and groups them by the operation that caused them, newest first.
/// </summary>
public sealed partial class CommandLogViewModel : ObservableObject, IDisposable
{
    private const int MaxEntries = 1000;

    private readonly List<OperationEntry> all = [];
    private readonly Dictionary<long, OperationEntry> byId = [];
    private readonly IAppShell shell;

    public CommandLogViewModel(GitRunner git, IAppShell shell)
    {
        this.shell = shell;
        git.Invoked += invocation => Dispatcher.UIThread.Post(() => this.Add(invocation));
        GitRunner.OperationFailed += this.OnOperationFailed;
    }

    public void Dispose() => GitRunner.OperationFailed -= this.OnOperationFailed;

    private void OnOperationFailed(GitOperation operation, string message) => Dispatcher.UIThread.Post(() => this.MarkFailed(operation, message));

    /// <summary>Rows that pass the filter, newest first.</summary>
    public ObservableCollection<OperationEntry> Entries { get; } = [];

    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    [ObservableProperty]
    public partial string Filter { get; set; } = "";

    [ObservableProperty]
    public partial bool ErrorsOnly { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasErrors), nameof(StatusText))]
    public partial int ErrorCount { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    public partial int OperationCount { get; private set; }

    public bool HasErrors => this.ErrorCount > 0;

    // Counts what the user did; background refreshes are listed but not counted, so "0 operations"
    // beside a log full of refreshes read as a contradiction. Nothing is said until there is something.
    public string StatusText => string.Join(" · ", new[]
    {
        this.OperationCount == 0 ? null : $"{this.OperationCount} {(this.OperationCount == 1 ? "operation" : "operations")}",
        this.ErrorCount == 0 ? null : $"{this.ErrorCount} {(this.ErrorCount == 1 ? "error" : "errors")}",
    }.OfType<string>());

    public bool IsEmpty => this.Entries.Count == 0;

    partial void OnFilterChanged(string value) => this.Rebuild();

    partial void OnErrorsOnlyChanged(bool value) => this.Rebuild();

    [RelayCommand]
    private void Toggle() => this.IsOpen = !this.IsOpen;

    [RelayCommand]
    private void ToggleEntry(OperationEntry entry) => entry.IsExpanded = !entry.IsExpanded;

    [RelayCommand]
    private Task CopyCall(CommandCall call) => this.shell.SetClipboardTextAsync(call.ToClipboardText());

    [RelayCommand]
    private Task CopyEntry(OperationEntry entry) => this.shell.SetClipboardTextAsync(entry.ToExportText());

    [RelayCommand]
    private async Task Export()
    {
        var text = this.ExportText(anonymizer: null);
        await this.shell.SaveTextFileAsync("Export command log", $"cheesegit-log-{DateTime.Now:yyyyMMdd-HHmmss}.txt", text);
    }

    /// <summary>
    /// Adds what the log knows to be private: the names in labels, repository
    /// folders, refs and paths in arguments. The log outlives the state it shows
    /// (a staged file that was committed since, a deleted branch), so the
    /// repository's current state alone would miss them.
    /// </summary>
    internal void AddPrivateNames(Anonymizer anonymizer)
    {
        foreach (var entry in this.all)
        {
            if (entry.WorkingDirectory.Length > 0) anonymizer.AddPath(entry.WorkingDirectory);
            foreach (var subject in entry.Subjects) anonymizer.AddPath(subject);
            foreach (var call in entry.Calls)
            {
                var (refs, paths) = (false, false);
                foreach (var arg in call.Args)
                {
                    if (paths) anonymizer.AddPath(arg);
                    else if (refs && arg != "--") anonymizer.AddRef(arg);
                    if (arg == "--") paths = true;
                    else if (arg == "--end-of-options") refs = true;
                }
            }
        }
    }

    /// <summary>The whole log, oldest first.</summary>
    internal string ExportText(Anonymizer? anonymizer) =>
        string.Concat(this.all.AsEnumerable().Reverse().Select(e => e.ToExportText(anonymizer) + "\n"));

    [RelayCommand]
    private void Clear()
    {
        this.all.Clear();
        this.byId.Clear();
        this.Entries.Clear();
        this.ErrorCount = 0;
        this.OperationCount = 0;
        this.OnPropertyChanged(nameof(this.IsEmpty));
    }

    internal void Add(GitInvocation invocation)
    {
        var op = invocation.Operation;
        var id = op?.Id ?? -invocation.Started.UtcTicks;
        var call = new CommandCall(
            invocation.Args,
            FormatCommand(invocation.Args),
            invocation.ExitCode,
            invocation.Started,
            invocation.Elapsed,
            Encoding.UTF8.GetString(invocation.Stderr).TrimEnd());

        if (!this.byId.TryGetValue(id, out var entry))
        {
            var label = op?.Label ?? "git";
            var background = op?.Background ?? true;
            var repository = Path.GetFileName(invocation.WorkingDirectory.TrimEnd('/', '\\'));
            if (background && this.FoldTarget(label, repository) is { } folded)
            {
                // Move the folded row to the top: it is the newest activity now.
                this.byId.Remove(folded.Id);
                this.all.Remove(folded);
                this.all.Add(folded);
                if (this.Entries.Remove(folded)) this.Entries.Insert(0, folded);
                folded.Repeat(id, op?.Started ?? invocation.Started);
                entry = folded;
            }
            else
            {
                entry = this.NewEntry(id, label, repository, background, op?.Started ?? invocation.Started, op?.Subjects, invocation.WorkingDirectory);
            }
            this.byId[id] = entry;
        }

        entry.Add(call);
        this.OnPropertyChanged(nameof(this.IsEmpty));
    }

    /// <summary>
    /// Background runs since the last user action fold into one row per label
    /// (a refresh, its diff and stats reads interleave, so only folding
    /// neighbours would still bury the user's actions), unless a run failed:
    /// errors stay visible.
    /// </summary>
    private OperationEntry? FoldTarget(string label, string repository)
    {
        for (var i = this.all.Count - 1; i >= 0; i--)
        {
            var entry = this.all[i];
            if (!entry.Background) return null;
            if (entry.Label == label && entry.Repository == repository) return entry.Failed ? null : entry;
        }
        return null;
    }

    internal void MarkFailed(GitOperation operation, string message)
    {
        // An operation can fail before its first git call (e.g. a folder that is not a repository).
        if (!this.byId.TryGetValue(operation.Id, out var entry))
        {
            entry = this.NewEntry(operation.Id, operation.Label, "", operation.Background, operation.Started, operation.Subjects);
            this.byId[operation.Id] = entry;
        }
        if (entry.Failed) return;
        entry.MarkFailed(message);
        this.ErrorCount++;
        if (this.ErrorsOnly && !this.Entries.Contains(entry) && this.Matches(entry)) this.Entries.Insert(0, entry);
        this.OnPropertyChanged(nameof(this.IsEmpty));
    }

    private OperationEntry NewEntry(long id, string label, string repository, bool background, DateTimeOffset started, IReadOnlyList<string>? subjects = null, string workingDirectory = "")
    {
        var entry = new OperationEntry(id, label, repository, background, started) { Subjects = subjects ?? [], WorkingDirectory = workingDirectory };
        this.all.Add(entry);
        if (!background) this.OperationCount++;
        if (this.all.Count > MaxEntries)
        {
            var dropped = this.all[0];
            this.all.RemoveAt(0);
            this.byId.Remove(dropped.Id);
            this.Entries.Remove(dropped);
            if (dropped.Failed) this.ErrorCount--;
        }
        if (this.Matches(entry)) this.Entries.Insert(0, entry);
        return entry;
    }

    private void Rebuild()
    {
        this.Entries.Clear();
        for (var i = this.all.Count - 1; i >= 0; i--)
        {
            if (this.Matches(this.all[i])) this.Entries.Add(this.all[i]);
        }
        this.OnPropertyChanged(nameof(this.IsEmpty));
    }

    private bool Matches(OperationEntry entry)
    {
        if (this.ErrorsOnly && !entry.Failed) return false;
        var term = this.Filter.Trim();
        if (term.Length == 0) return true;
        return entry.Label.Contains(term, StringComparison.OrdinalIgnoreCase)
            || (entry.Error?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)
            || entry.Repository.Contains(term, StringComparison.OrdinalIgnoreCase)
            || entry.Calls.Any(c => c.CommandLine.Contains(term, StringComparison.OrdinalIgnoreCase) || c.Stderr.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Shows the command as the user would type it, without the hardened
    /// <c>-c</c> overrides every call carries (they would bury the actual command).
    /// </summary>
    internal static string FormatCommand(IReadOnlyList<string> args)
    {
        var sb = new StringBuilder("git");
        foreach (var arg in args)
        {
            sb.Append(' ');
            var needsQuotes = arg.Length == 0 || arg.Any(c => char.IsWhiteSpace(c) || c is '"' or '\'' or '$' or '`' or '\\');
            sb.Append(needsQuotes ? "'" + arg.Replace("'", "'\\''") + "'" : arg);
        }
        return DisplayText.Escape(sb.ToString());
    }
}
