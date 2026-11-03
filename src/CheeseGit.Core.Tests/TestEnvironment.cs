namespace CheeseGit.Core.Tests;

/// <summary>
/// Isolates every git process started by the tests from the machine's config
/// and applies one config profile from the hostile-environment matrix (plan
/// §9). The profile is chosen with <c>CHEESEGIT_TEST_PROFILE</c>; run
/// <c>test-matrix.sh</c> to run the whole suite under each of them.
/// </summary>
public static class TestEnvironment
{
    /// <summary>
    /// Each profile is a set of user-level settings. "hostile" bundles the
    /// presentation settings that broke the old app's parsers; the others are
    /// semantic settings real users have that change git's behaviour.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string[]> Profiles = new Dictionary<string, string[]>
    {
        ["default"] = [],
        ["hostile"] =
        [
            "color.ui=always",
            "color.diff=always",
            "color.status=always",
            "color.branch=always",
            "color.decorate=always",
            "column.ui=always",
            "core.quotepath=true",
            "diff.noprefix=true",
            "diff.mnemonicPrefix=true",
            "diff.relative=true",
            "diff.external=false",
            "diff.suppressBlankEmpty=true",
            "diff.colorMoved=zebra",
            "diff.submodule=log",
            "status.relativePaths=true",
            "status.short=true",
            "status.branch=true",
            "status.aheadBehind=false",
            "status.showStash=true",
            "log.showSignature=true",
            "log.decorate=full",
            "log.abbrevCommit=true",
            "format.pretty=oneline",
            "i18n.logOutputEncoding=ISO-8859-1",
            "apply.whitespace=error",
            "stash.showPatch=true",
            "stash.showIncludeUntracked=true",
            "branch.sort=refname",
            "core.pager=less",
            "init.defaultBranch=trunk",
            "advice.detachedHead=true",
            "commit.verbose=true",
            "core.abbrev=12",
        ],
        ["crlf"] = ["core.autocrlf=true"],
        ["crlf-input"] = ["core.autocrlf=input"],
        ["merge"] =
        [
            "merge.conflictStyle=zdiff3",
            "merge.ff=false",
            "rerere.enabled=true",
            "rerere.autoUpdate=true",
            "pull.rebase=false",
        ],
        ["status"] =
        [
            "status.showUntrackedFiles=no",
            "status.renames=false",
            "diff.renames=copies",
            "core.untrackedCache=true",
            "core.ignoreCase=false",
        ],
    };

    public static string Profile { get; }

    public static string GlobalConfigPath { get; }

    static TestEnvironment()
    {
        Profile = Environment.GetEnvironmentVariable("CHEESEGIT_TEST_PROFILE") is { Length: > 0 } p ? p : "hostile";
        if (!Profiles.TryGetValue(Profile, out var settings))
        {
            throw new InvalidOperationException($"Unknown CHEESEGIT_TEST_PROFILE '{Profile}'. Known: {string.Join(", ", Profiles.Keys)}");
        }

        var dir = Directory.CreateTempSubdirectory("cheesegit-test-home-").FullName;
        GlobalConfigPath = Path.Combine(dir, "gitconfig");
        var lines = new List<string>
        {
            "[user]", "\tname = Test User", "\temail = test@test.com",
            "[commit]", "\tgpgsign = false",
            "[tag]", "\tgpgsign = false",
            "[protocol \"file\"]", "\tallow = always",
        };
        foreach (var kv in settings)
        {
            var eq = kv.IndexOf('=');
            var key = kv[..eq];
            var dot = key.LastIndexOf('.');
            lines.Add($"[{key[..dot]}]");
            lines.Add($"\t{key[(dot + 1)..]} = {kv[(eq + 1)..]}");
        }
        File.WriteAllLines(GlobalConfigPath, lines);

        // Inherited by every git process, including the runner's.
        Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", GlobalConfigPath);
        Environment.SetEnvironmentVariable("GIT_CONFIG_NOSYSTEM", "1");
        Environment.SetEnvironmentVariable("HOME", dir);
        // A non-English locale: the runner must pin LC_ALL=C regardless.
        Environment.SetEnvironmentVariable("LANG", "de_DE.UTF-8");
        Environment.SetEnvironmentVariable("LC_ALL", "de_DE.UTF-8");
    }

    /// <summary>Forces the static constructor to run before any git process starts.</summary>
    public static void EnsureInitialized()
    {
    }

    public static bool Has(string setting) => Profiles[Profile].Contains(setting);
}
