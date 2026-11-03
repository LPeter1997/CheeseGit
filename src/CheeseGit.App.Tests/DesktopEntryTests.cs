using Avalonia;
using Avalonia.Headless.XUnit;
using CheeseGit.App.Services;
using CheeseGit.App.ViewModels;
using CheeseGit.Core.Git;

namespace CheeseGit.App.Tests;

/// <summary>The AppImage's applications-menu entry (the old app's <c>desktop_entry</c> module).</summary>
public sealed class DesktopEntryTests : IDisposable
{
    private readonly string dataHome = Directory.CreateTempSubdirectory("cheesegit-xdg-").FullName;

    [Fact]
    public void NotAnAppImageIsNotApplicable() =>
        Assert.Equal(DesktopEntryStatus.NotApplicable, DesktopEntry.Check(null, this.dataHome));

    [Fact]
    public void RegisterMakesItCurrentAndAMoveMakesItStale()
    {
        Assert.Equal(DesktopEntryStatus.Missing, DesktopEntry.Check("/opt/apps/CheeseGit.AppImage", this.dataHome));
        DesktopEntry.Register("/opt/apps/CheeseGit.AppImage", this.dataHome, [1, 2, 3]);
        Assert.Equal(DesktopEntryStatus.Current, DesktopEntry.Check("/opt/apps/CheeseGit.AppImage", this.dataHome));
        Assert.Equal(DesktopEntryStatus.Stale, DesktopEntry.Check("/home/me/CheeseGit.AppImage", this.dataHome));

        var text = File.ReadAllText(Path.Combine(this.dataHome, "applications", "cheesegit.desktop"));
        Assert.Contains("Exec=/opt/apps/CheeseGit.AppImage %F\n", text);
        Assert.Contains("StartupWMClass=io.github.LPeter1997.CheeseGit\n", text);
        var icon = Path.Combine(this.dataHome, "icons", "hicolor", "512x512", "apps", "io.github.LPeter1997.CheeseGit.png");
        Assert.Contains($"Icon={icon}\n", text);
        Assert.Equal([1, 2, 3], File.ReadAllBytes(icon));
    }

    [Fact]
    public void TheOldAppsEntryIsStale()
    {
        Directory.CreateDirectory(Path.Combine(this.dataHome, "applications"));
        File.WriteAllText(Path.Combine(this.dataHome, "applications", "cheesegit.desktop"),
            "[Desktop Entry]\nName=CheeseGit\nExec=/tmp/.mount_CheeseabcXYZ/usr/bin/cheesegit\nStartupWMClass=cheesegit\n");
        Assert.Equal(DesktopEntryStatus.Stale, DesktopEntry.Check("/opt/CheeseGit.AppImage", this.dataHome));
    }

    [Theory]
    [InlineData("/opt/CheeseGit.AppImage", "/opt/CheeseGit.AppImage %F")]
    [InlineData("/home/me/My Apps/CheeseGit.AppImage", "\"/home/me/My Apps/CheeseGit.AppImage\" %F")]
    [InlineData("/home/me/a$b `c`/x", "\"/home/me/a\\\\$b \\\\`c\\\\`/x\" %F")]
    [InlineData("/home/me/100%/x", "/home/me/100%%/x %F")]
    public void ExecQuoting(string program, string expected) => Assert.Equal(expected, DesktopEntry.ExecLine(program));

    [Fact]
    public void ADesktopFileValidatorAcceptsTheEntry()
    {
        // desktop-file-validate is part of desktop-file-utils; skip where it is not installed.
        var validator = Environment.GetEnvironmentVariable("PATH")?.Split(':').Select(d => Path.Combine(d, "desktop-file-validate")).FirstOrDefault(File.Exists);
        if (validator is null) return;
        DesktopEntry.Register("/home/me/My Apps/a$b.AppImage", this.dataHome, [1]);
        var psi = new System.Diagnostics.ProcessStartInfo(validator, Path.Combine(this.dataHome, "applications", "cheesegit.desktop")) { RedirectStandardOutput = true, RedirectStandardError = true };
        using var p = System.Diagnostics.Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit();
        Assert.True(p.ExitCode == 0 && !output.Contains("error", StringComparison.OrdinalIgnoreCase), output);
    }

    private MainViewModel Main(AppSettings? settings = null) =>
        new(SettingsStore.InMemory(settings), ((App)Application.Current!).Themes, new GitRunner(), "test")
        {
            AppImagePath = "/opt/CheeseGit.AppImage",
            DataHome = this.dataHome,
        };

    [AvaloniaFact]
    public void AMissingEntryAsksAndTheActionRegisters()
    {
        using var main = this.Main();
        main.CheckDesktopEntry();
        Assert.True(main.CanRegisterDesktopEntry);
        var alert = Assert.Single(main.Alerts);
        Assert.Equal("Add", alert.ActionLabel);
        alert.RunActionCommand.Execute(null);
        Assert.Equal(DesktopEntryStatus.Current, main.DesktopEntryStatus);
        Assert.False(main.CanRegisterDesktopEntry);
    }

    [AvaloniaFact]
    public void DismissingIsRemembered()
    {
        using (var main = this.Main())
        {
            main.CheckDesktopEntry();
            Assert.Single(main.Alerts).DismissCommand.Execute(null);
            Assert.True(main.Settings.DesktopEntryDismissed);
        }
        using (var main = this.Main(new AppSettings { DesktopEntryDismissed = true }))
        {
            main.CheckDesktopEntry();
            Assert.Empty(main.Alerts);
            Assert.True(main.CanRegisterDesktopEntry); // The options menu still offers it.
        }
    }

    public void Dispose() => Directory.Delete(this.dataHome, recursive: true);
}
