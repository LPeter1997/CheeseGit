using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace CheeseGit.Core.Git;

public enum AskpassKind : byte
{
    /// <summary>Shown as typed (a user name).</summary>
    Text,

    /// <summary>Hidden (a password, token or key passphrase).</summary>
    Secret,

    /// <summary>A yes/no question (ssh's unknown host key).</summary>
    Confirm,
}

/// <summary>A question git or ssh asks while it runs, with the prompt text they wrote.</summary>
public sealed record AskpassRequest(string Prompt, AskpassKind Kind)
{
    /// <summary>
    /// Askpass gets only the prompt text, so its kind is read from it: ssh's host-key question
    /// ends in "(yes/no/[fingerprint])?", git asks "Username for '…': ". Everything else is
    /// treated as a secret, so an unknown prompt never shows what is typed.
    /// </summary>
    public static AskpassKind Classify(string prompt, string? sshAskpassPrompt) =>
        sshAskpassPrompt == "confirm" || prompt.Contains("(yes/no", StringComparison.Ordinal) ? AskpassKind.Confirm
        : prompt.StartsWith("Username", StringComparison.Ordinal) ? AskpassKind.Text
        : AskpassKind.Secret;
}

/// <summary>
/// Plan §6.6: our own executable is git's <c>GIT_ASKPASS</c> and ssh's <c>SSH_ASKPASS</c>. git
/// runs it with the prompt as its only argument; it forwards the prompt over a local socket to
/// the running app, which asks the user, and prints the answer. Nothing is written to disk, and
/// credential helpers the user configured still answer first (git asks them before askpass).
/// </summary>
public static class Askpass
{
    public const string SocketVariable = "CHEESEGIT_ASKPASS_SOCKET";
    public const string TokenVariable = "CHEESEGIT_ASKPASS_TOKEN";

    /// <summary>
    /// Runs the askpass helper when this process was started as one (the socket and token
    /// variables are set and there is one argument, the prompt). Returns its exit code, or null
    /// when the process is not a helper and should start normally.
    /// </summary>
    public static int? TryRunHelper(string[] args)
    {
        var socket = Environment.GetEnvironmentVariable(SocketVariable);
        var token = Environment.GetEnvironmentVariable(TokenVariable);
        if (string.IsNullOrEmpty(socket) || string.IsNullOrEmpty(token) || args.Length != 1) return null;
        var sshPrompt = Environment.GetEnvironmentVariable("SSH_ASKPASS_PROMPT");
        // "none": ssh only informs (e.g. "touch your security key") and kills the helper later.
        if (sshPrompt == "none") return 0;
        try
        {
            var answer = AskAsync(socket, token, new AskpassRequest(args[0], AskpassRequest.Classify(args[0], sshPrompt))).GetAwaiter().GetResult();
            if (answer is null) return 1; // Cancelled: git and ssh treat a failing helper as "no answer".
            using var stdout = Console.OpenStandardOutput();
            stdout.Write(Encoding.UTF8.GetBytes(answer + "\n"));
            return 0;
        }
        catch (Exception e) when (e is IOException or SocketException)
        {
            Console.Error.WriteLine("CheeseGit is not reachable to ask: " + e.Message);
            return 1;
        }
    }

    /// <summary>The helper's side: sends the request and waits for the answer (null: cancelled).</summary>
    public static async Task<string?> AskAsync(string socketPath, string token, AskpassRequest request, CancellationToken ct = default)
    {
        using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), ct);
        await using var stream = new NetworkStream(socket, ownsSocket: false);
        await WriteStringAsync(stream, token, ct);
        stream.WriteByte((byte)request.Kind);
        await WriteStringAsync(stream, request.Prompt, ct);
        await stream.FlushAsync(ct);
        var status = new byte[1];
        try
        {
            // Async with the token: a synchronous read could not be cancelled.
            if (await stream.ReadAsync(status, ct) == 0 || status[0] != 0) return null;
        }
        catch (IOException)
        {
            return null; // Hung up on (a wrong token): no answer.
        }
        return await ReadStringAsync(stream, ct);
    }

    internal static async Task WriteStringAsync(Stream stream, string text, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        await stream.WriteAsync(BitConverter.GetBytes(bytes.Length), ct);
        await stream.WriteAsync(bytes, ct);
    }

    internal static async Task<string> ReadStringAsync(Stream stream, CancellationToken ct)
    {
        var length = new byte[4];
        await stream.ReadExactlyAsync(length, ct);
        var count = BitConverter.ToInt32(length);
        if (count is < 0 or > 1 << 20) throw new IOException("Bad askpass message.");
        var bytes = new byte[count];
        await stream.ReadExactlyAsync(bytes, ct);
        return Encoding.UTF8.GetString(bytes);
    }
}

/// <summary>
/// The app's side of the askpass bridge: a Unix domain socket (Windows 10+ has them too) in a
/// folder only this user can read, and a random token the helper must present. Each connection is
/// one question for <c>handler</c>; when the helper goes away (git was cancelled), the handler's
/// token is cancelled so its dialog can close.
/// </summary>
public sealed class AskpassServer : IDisposable
{
    private readonly Func<AskpassRequest, CancellationToken, Task<string?>> handler;
    private readonly Socket listener;
    private readonly CancellationTokenSource stop = new();
    private readonly string directory;
    private readonly byte[] token = RandomNumberGenerator.GetBytes(32);

    /// <param name="helperPath">The executable git and ssh run; it must call <see cref="Askpass.TryRunHelper"/> first thing.</param>
    public AskpassServer(string helperPath, Func<AskpassRequest, CancellationToken, Task<string?>> handler)
    {
        this.handler = handler;
        this.directory = CreatePrivateDirectory();
        this.SocketPath = Path.Combine(this.directory, "askpass.sock");
        this.listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        this.listener.Bind(new UnixDomainSocketEndPoint(this.SocketPath));
        this.listener.Listen(16);
        var tokenText = Convert.ToHexString(this.token);
        this.Environment = new Dictionary<string, string>
        {
            ["GIT_ASKPASS"] = helperPath,
            ["SSH_ASKPASS"] = helperPath,
            // OpenSSH 8.4+: use the helper even with a terminal (started from a shell, ssh would
            // otherwise ask on it and hang where nobody looks).
            ["SSH_ASKPASS_REQUIRE"] = "force",
            [Askpass.SocketVariable] = this.SocketPath,
            [Askpass.TokenVariable] = tokenText,
        };
        _ = Task.Run(this.AcceptLoopAsync);
    }

    public string SocketPath { get; }

    /// <summary>The variables that route git's and ssh's questions here; see <see cref="Configure"/>.</summary>
    public IReadOnlyDictionary<string, string> Environment { get; }

    public void Configure(GitRunner git)
    {
        foreach (var (key, value) in this.Environment) git.Environment[key] = value;
    }

    /// <summary>
    /// Socket paths are limited to about 100 bytes, so a long temp path cannot hold one. The
    /// per-user runtime folder is short and private; otherwise a folder only we can enter.
    /// </summary>
    private static string CreatePrivateDirectory()
    {
        var name = "cheesegit-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant();
        string[] roots = OperatingSystem.IsWindows()
            ? [Path.GetTempPath()]
            : [System.Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") ?? "", Path.GetTempPath(), "/tmp"];
        foreach (var root in roots.Where(r => r.Length > 0 && Directory.Exists(r)))
        {
            var dir = Path.Combine(root, name);
            if (Path.Combine(dir, "askpass.sock").Length > 100) continue;
            if (OperatingSystem.IsWindows()) Directory.CreateDirectory(dir);
            else Directory.CreateDirectory(dir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            return dir;
        }
        throw new IOException("No folder with a short enough path for the askpass socket.");
    }

    private async Task AcceptLoopAsync()
    {
        while (!this.stop.IsCancellationRequested)
        {
            Socket client;
            try
            {
                client = await this.listener.AcceptAsync(this.stop.Token);
            }
            catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                return;
            }
            _ = Task.Run(() => this.ServeAsync(client));
        }
    }

    private async Task ServeAsync(Socket client)
    {
        using var _ = client;
        await using var stream = new NetworkStream(client, ownsSocket: false);
        try
        {
            using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(this.stop.Token);
            readTimeout.CancelAfter(TimeSpan.FromSeconds(10));
            var token = await Askpass.ReadStringAsync(stream, readTimeout.Token);
            if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(token), Encoding.UTF8.GetBytes(Convert.ToHexString(this.token)))) return;
            var kindByte = new byte[1];
            await stream.ReadExactlyAsync(kindByte, readTimeout.Token);
            var prompt = await Askpass.ReadStringAsync(stream, readTimeout.Token);
            var kind = Enum.IsDefined((AskpassKind)kindByte[0]) ? (AskpassKind)kindByte[0] : AskpassKind.Secret;

            // The helper sends nothing more: a read that ends means it was killed (git cancelled).
            using var gone = CancellationTokenSource.CreateLinkedTokenSource(this.stop.Token);
            var watch = WatchAsync(stream, gone);
            var answer = await this.handler(new AskpassRequest(prompt, kind), gone.Token);
            gone.Cancel();
            await watch;
            if (answer is null)
            {
                stream.WriteByte(1);
            }
            else
            {
                stream.WriteByte(0);
                await Askpass.WriteStringAsync(stream, answer, CancellationToken.None);
            }
            await stream.FlushAsync();
        }
        catch (Exception e) when (e is IOException or SocketException or OperationCanceledException)
        {
            // The helper went away or sent garbage: nothing to answer.
        }
    }

    private static async Task WatchAsync(Stream stream, CancellationTokenSource gone)
    {
        try
        {
            var buffer = new byte[1];
            // Any byte or the end of the stream: the helper is not waiting any more.
            var read = await stream.ReadAsync(buffer, gone.Token);
            if (read >= 0) gone.Cancel();
        }
        catch (Exception e) when (e is IOException or SocketException or OperationCanceledException)
        {
            gone.Cancel();
        }
    }

    public void Dispose()
    {
        this.stop.Cancel();
        this.listener.Dispose();
        try
        {
            Directory.Delete(this.directory, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A leftover empty folder in a per-user temp location.
        }
    }
}
