using System.Net;
using System.Net.Sockets;
using System.Text;
using Avalonia.Headless.XUnit;
using CheeseGit.App.ViewModels;
using CheeseGit.Core.Git;
using CheeseGit.Core.Tests;

namespace CheeseGit.App.Tests;

/// <summary>Plan §6.6 in the app: git's questions become dialogs, answers can be remembered for the session.</summary>
public class AskpassTests
{
    private static string Helper => Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "cheesegit-core.exe" : "cheesegit-core");

    /// <summary>Bounds the fetch: unbounded, a fetch that never ended hung the whole run on Windows.</summary>
    private static readonly TimeSpan FetchTimeout = TimeSpan.FromSeconds(60);

    /// <summary>
    /// The fetch's result. Questions beyond the ones the test answered are cancelled and fail the test with their
    /// prompts: on Windows and macOS a failed fetch went on to a prune dry run that asked again.
    /// </summary>
    private static async Task<bool> FinishAsync(AppHarness app, Task<bool> fetching, BasicAuthServer server)
    {
        var extra = new List<string>();
        var deadline = DateTime.UtcNow + FetchTimeout;
        while (!fetching.IsCompleted)
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException($"The fetch did not finish. Extra questions: [{string.Join("; ", extra)}]. Requests: [{string.Join("; ", server.Requests)}]");
            if (app.Main.Dialog is AskpassDialogViewModel question)
            {
                extra.Add(question.Prompt);
                question.CancelCommand.Execute(null);
            }
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
        Assert.True(extra.Count == 0, $"git asked again: [{string.Join("; ", extra)}]. Requests: [{string.Join("; ", server.Requests)}]");
        return await fetching;
    }

    private static readonly AskpassRequest Password = new("Password for 'https://alice@example.com': ", AskpassKind.Secret);

    [AvaloniaFact]
    public async Task A_fetch_that_needs_credentials_asks_in_dialogs_and_sends_the_answers()
    {
        using var server = new BasicAuthServer();
        using var repo = TempRepo.Init();
        repo.CommitFile("a.txt", "a\n", "first");
        repo.Raw("remote", "add", "origin", server.Url);
        // As on Windows and macOS: a failed fetch there went on to a case-collision prune dry run, which asked again.
        repo.Raw("config", "core.ignorecase", "true");
        using var app = await AppHarness.OpenAsync(repo);
        app.Main.EnableAskpass(Helper);

        var fetching = app.Tab.RunAsync("Fetch", (r, ct) => r.FetchAsync("origin", ct: ct));
        await AppHarness.Until(() => app.Main.Dialog is AskpassDialogViewModel, "the user name question");
        var user = (AskpassDialogViewModel)app.Main.Dialog!;
        Assert.True(user.IsText);
        Assert.StartsWith("Username for 'http://127.0.0.1:", user.Prompt);
        user.Answer = "alice";
        user.SubmitCommand.Execute(null);

        await AppHarness.Until(() => app.Main.Dialog is AskpassDialogViewModel { IsSecret: true }, "the password question");
        var password = (AskpassDialogViewModel)app.Main.Dialog!;
        password.Answer = "s3cret";
        password.SubmitCommand.Execute(null);

        // The server takes the credentials and then has no repository: the fetch fails after authenticating.
        Assert.False(await FinishAsync(app, fetching, server));
        Assert.Contains("alice:s3cret", server.Credentials);
        Assert.Null(app.Main.Dialog);
    }

    [AvaloniaFact]
    public async Task Cancelling_the_question_fails_the_fetch_without_sending_anything()
    {
        using var server = new BasicAuthServer();
        using var repo = TempRepo.Init();
        repo.CommitFile("a.txt", "a\n", "first");
        repo.Raw("remote", "add", "origin", server.Url);
        // As on Windows and macOS: a failed fetch there went on to a case-collision prune dry run, which asked again.
        repo.Raw("config", "core.ignorecase", "true");
        using var app = await AppHarness.OpenAsync(repo);
        app.Main.EnableAskpass(Helper);

        var fetching = app.Tab.RunAsync("Fetch", (r, ct) => r.FetchAsync("origin", ct: ct));
        await AppHarness.Until(() => app.Main.Dialog is AskpassDialogViewModel, "the user name question");
        app.Main.Dialog!.CancelCommand.Execute(null);
        Assert.False(await FinishAsync(app, fetching, server));
        Assert.Empty(server.Credentials);
        Assert.Single(app.Main.Alerts);
    }

    [AvaloniaFact]
    public async Task Remembered_answers_are_reused_and_forgotten_when_an_operation_fails()
    {
        using var app = AppHarness.CreateMain();
        var first = app.AskAsync(Password, CancellationToken.None);
        var dialog = Assert.IsType<AskpassDialogViewModel>(app.Dialog);
        dialog.Answer = "s3cret";
        dialog.Remember = true;
        dialog.SubmitCommand.Execute(null);
        Assert.Equal("s3cret", await first);

        // Asked again: answered without a dialog.
        var start = DateTimeOffset.Now;
        Assert.Equal("s3cret", await app.AskAsync(Password, CancellationToken.None));
        Assert.Null(app.Dialog);

        // The operation that used it failed: it may be wrong, so it is asked for again.
        app.ForgetAnswersUsedSince(start);
        var again = app.AskAsync(Password, CancellationToken.None);
        Assert.IsType<AskpassDialogViewModel>(app.Dialog).CancelCommand.Execute(null);
        Assert.Null(await again);
    }

    [AvaloniaFact]
    public async Task A_question_covers_the_open_dialog_and_gives_it_back()
    {
        using var app = AppHarness.CreateMain();
        var confirm = app.ConfirmAsync("Add the remote?", "Add");
        var covered = app.Dialog;

        var asking = app.AskAsync(new AskpassRequest("The authenticity of host 'example.com' can't be established.\nAre you sure you want to continue connecting (yes/no/[fingerprint])? ", AskpassKind.Confirm), CancellationToken.None);
        var question = Assert.IsType<AskpassDialogViewModel>(app.Dialog);
        Assert.Equal("Unknown host", question.Title);
        Assert.False(question.HasField);
        question.SubmitCommand.Execute(null);
        Assert.Equal("yes", await asking);

        Assert.Same(covered, app.Dialog);
        ((ConfirmDialogViewModel)covered!).ConfirmCommand.Execute(null);
        Assert.True(await confirm);
        Assert.Null(app.Dialog);
    }

    [AvaloniaFact]
    public async Task A_withdrawn_question_closes_its_dialog()
    {
        using var app = AppHarness.CreateMain();
        using var cts = new CancellationTokenSource();
        var asking = app.AskAsync(Password, cts.Token);
        Assert.IsType<AskpassDialogViewModel>(app.Dialog);
        cts.Cancel();
        await AppHarness.Until(() => app.Dialog is null, "the dialog to close");
        Assert.Null(await asking);
    }

    /// <summary>An HTTP server that demands Basic credentials and records them; it has no repositories.</summary>
    private sealed class BasicAuthServer : IDisposable
    {
        private readonly HttpListener listener = new();
        private readonly List<string> credentials = [];
        private readonly List<string> requests = [];

        public BasicAuthServer()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            this.Url = $"http://127.0.0.1:{port}/repo.git";
            this.listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            this.listener.Start();
            _ = Task.Run(this.ServeAsync);
        }

        public string Url { get; }

        public List<string> Requests
        {
            get
            {
                lock (this.requests) return [.. this.requests];
            }
        }

        public List<string> Credentials
        {
            get
            {
                lock (this.credentials) return [.. this.credentials];
            }
        }

        private async Task ServeAsync()
        {
            while (this.listener.IsListening)
            {
                HttpListenerContext context;
                try
                {
                    context = await this.listener.GetContextAsync();
                }
                catch (Exception e) when (e is HttpListenerException or ObjectDisposedException or InvalidOperationException)
                {
                    return;
                }
                lock (this.requests) this.requests.Add($"{context.Request.HttpMethod} {context.Request.Url?.PathAndQuery} auth={context.Request.Headers["Authorization"] is not null}");
                if (context.Request.Headers["Authorization"] is { } auth && auth.StartsWith("Basic ", StringComparison.Ordinal))
                {
                    lock (this.credentials) this.credentials.Add(Encoding.UTF8.GetString(Convert.FromBase64String(auth[6..])));
                    context.Response.StatusCode = 404;
                }
                else
                {
                    context.Response.StatusCode = 401;
                    context.Response.AddHeader("WWW-Authenticate", "Basic realm=\"test\"");
                }
                context.Response.Close();
            }
        }

        public void Dispose()
        {
            this.listener.Stop();
            this.listener.Close();
        }
    }
}
