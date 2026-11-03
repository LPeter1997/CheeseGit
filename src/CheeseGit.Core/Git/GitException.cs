namespace CheeseGit.Core.Git;

/// <summary>Base class for everything the git layer reports as a failure.</summary>
public abstract class GitException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// git exited with a non-zero code. <see cref="GitResult.StderrText"/> is for
/// showing to the user; code must not branch on it.
/// </summary>
public sealed class GitCommandException(string operation, GitResult result)
    : GitException($"{operation} failed (git exited with {result.ExitCode}): {result.StderrText}")
{
    public string Operation { get; } = operation;

    public GitResult Result { get; } = result;
}

/// <summary>Why an operation was refused before (or instead of) running git.</summary>
public enum GitErrorCode
{
    NotARepository,
    BareRepository,
    UnsupportedGitVersion,
    DetachedHead,
    InitialCommit,
    AlreadyPushed,
    NothingSelected,
    InvalidName,
    NotFullyMerged,
    NoUpstream,
    NotFound,
    Unsupported,
    NotInProgress,
    UnexpectedOutput,
    UnresolvedConflicts,
}

/// <summary>An operation was refused because the repository is not in a state that allows it.</summary>
public sealed class GitOperationException(GitErrorCode code, string message, Exception? inner = null) : GitException(message, inner)
{
    public GitErrorCode Code { get; } = code;
}

/// <summary>The git executable could not be started.</summary>
public sealed class GitNotFoundException(string executable, Exception inner)
    : GitException($"Could not start git ('{executable}'). Is git installed and on PATH?", inner);
