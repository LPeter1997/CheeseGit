using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using CheeseGit.Core.Git;

namespace CheeseGit.Core.Tests;

/// <summary>Plan §6.6: git's and ssh's questions reach the app through the askpass helper (the harness here, the app in use).</summary>
public class AskpassTests
{
    private static string Helper => Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "cheesegit-core.exe" : "cheesegit-core");

    private static readonly byte[] CredentialQuery = Encoding.UTF8.GetBytes("protocol=https\nhost=example.com\n\n");

    [Fact]
    public async Task Git_asks_for_a_user_name_and_password_through_the_helper()
    {
        var asked = new ConcurrentQueue<AskpassRequest>();
        using var server = new AskpassServer(Helper, (request, _) =>
        {
            asked.Enqueue(request);
            return Task.FromResult<string?>(request.Kind == AskpassKind.Text ? "alice" : "s3cret ü");
        });
        using var repo = TempRepo.Init();
        var git = new GitRunner();
        server.Configure(git);

        // credential.helper= : no helper of the user's may answer first.
        var result = await git.RunAsync(repo.Path, ["-c", "credential.helper=", "credential", "fill"], stdin: CredentialQuery);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("username=alice\n", result.StdoutText);
        Assert.Contains("password=s3cret ü\n", result.StdoutText);
        Assert.Equal(
            [new AskpassRequest("Username for 'https://example.com': ", AskpassKind.Text), new AskpassRequest("Password for 'https://alice@example.com': ", AskpassKind.Secret)],
            asked);
    }

    [Fact]
    public async Task Cancelling_the_question_fails_the_git_command()
    {
        using var server = new AskpassServer(Helper, (_, _) => Task.FromResult<string?>(null));
        using var repo = TempRepo.Init();
        var git = new GitRunner();
        server.Configure(git);

        var result = await git.RunAsync(repo.Path, ["-c", "credential.helper=", "credential", "fill"], stdin: CredentialQuery);
        Assert.NotEqual(0, result.ExitCode);
        Assert.DoesNotContain("password=", result.StdoutText);
    }

    [Fact]
    public async Task A_wrong_token_is_never_asked()
    {
        var called = false;
        using var server = new AskpassServer(Helper, (_, _) =>
        {
            called = true;
            return Task.FromResult<string?>("secret");
        });
        Assert.Null(await Askpass.AskAsync(server.SocketPath, "not-the-token", new AskpassRequest("Password: ", AskpassKind.Secret)));
        Assert.False(called);
    }

    [Fact]
    public async Task The_question_is_withdrawn_when_the_helper_goes_away()
    {
        var withdrawn = new TaskCompletionSource();
        using var server = new AskpassServer(Helper, async (_, ct) =>
        {
            try
            {
                await Task.Delay(Timeout.Infinite, ct);
            }
            catch (OperationCanceledException)
            {
                withdrawn.SetResult();
            }
            return null;
        });

        // A helper killed with git (the user cancelled the fetch): it connects, asks, and vanishes.
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Askpass.AskAsync(server.SocketPath, server.Environment[Askpass.TokenVariable], new AskpassRequest("Password: ", AskpassKind.Secret), cts.Token));
        await withdrawn.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void The_socket_folder_is_private_and_removed()
    {
        string dir;
        using (var server = new AskpassServer(Helper, (_, _) => Task.FromResult<string?>(null)))
        {
            dir = Path.GetDirectoryName(server.SocketPath)!;
            Assert.True(server.SocketPath.Length <= 100);
            if (!OperatingSystem.IsWindows())
            {
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(dir));
            }
        }
        Assert.False(Directory.Exists(dir));
    }

    [Fact]
    public async Task Ssh_asks_for_a_key_passphrase_through_the_helper()
    {
        if (OperatingSystem.IsWindows() || !OnPath("ssh-agent") || !OnPath("ssh-add") || !OnPath("ssh-keygen")) return;
        using var dir = TempRepo.EmptyDirectory();
        var key = Path.Combine(dir.Path, "key");
        Run("ssh-keygen", ["-q", "-t", "ed25519", "-N", "pass phrase", "-C", "test", "-f", key], null);
        // The agent picks its own short socket path; ours would be too long for a Unix socket.
        var agent = Run("ssh-agent", ["-s"], null);
        var sock = agent.Split(';', '\n').Select(p => p.Trim()).First(p => p.StartsWith("SSH_AUTH_SOCK=", StringComparison.Ordinal))["SSH_AUTH_SOCK=".Length..];
        var pid = agent.Split(';', '\n').Select(p => p.Trim()).First(p => p.StartsWith("SSH_AGENT_PID=", StringComparison.Ordinal))["SSH_AGENT_PID=".Length..];
        try
        {
            var asked = new ConcurrentQueue<AskpassRequest>();
            using var server = new AskpassServer(Helper, (request, _) =>
            {
                asked.Enqueue(request);
                return Task.FromResult<string?>("pass phrase");
            });
            var env = new Dictionary<string, string>(server.Environment) { ["SSH_AUTH_SOCK"] = sock };
            Run("ssh-add", [key], env);
            var request = Assert.Single(asked);
            Assert.Equal(AskpassKind.Secret, request.Kind);
            Assert.StartsWith("Enter passphrase for ", request.Prompt);
            Assert.Contains("test", Run("ssh-add", ["-l"], new Dictionary<string, string> { ["SSH_AUTH_SOCK"] = sock }));
        }
        finally
        {
            Run("kill", [pid], null);
        }
    }

    [Theory]
    [InlineData("Username for 'https://github.com': ", null, AskpassKind.Text)]
    [InlineData("Password for 'https://me@github.com': ", null, AskpassKind.Secret)]
    [InlineData("Enter passphrase for key '/home/me/.ssh/id_ed25519': ", null, AskpassKind.Secret)]
    [InlineData("The authenticity of host 'example.com' can't be established.\nED25519 key fingerprint is SHA256:abc.\nAre you sure you want to continue connecting (yes/no/[fingerprint])? ", null, AskpassKind.Confirm)]
    [InlineData("Allow use of key?", "confirm", AskpassKind.Confirm)]
    [InlineData("Something new: ", null, AskpassKind.Secret)]
    public void Prompts_are_classified(string prompt, string? sshPrompt, AskpassKind expected) =>
        Assert.Equal(expected, AskpassRequest.Classify(prompt, sshPrompt));

    private static bool OnPath(string tool) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator).Any(d => File.Exists(Path.Combine(d, tool)));

    /// <summary>Runs a tool without a terminal on stdin (as the app runs ssh), with extra variables.</summary>
    private static string Run(string tool, string[] args, IReadOnlyDictionary<string, string>? env)
    {
        var psi = new ProcessStartInfo(tool) { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in args) psi.ArgumentList.Add(a);
        if (env is not null) foreach (var (k, v) in env) psi.Environment[k] = v;
        using var p = Process.Start(psi)!;
        p.StandardInput.Close();
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        if (!p.WaitForExit(20_000)) p.Kill(true);
        Assert.True(p.ExitCode == 0, $"{tool} {string.Join(' ', args)} exited {p.ExitCode}: {stderr}");
        return stdout;
    }
}
