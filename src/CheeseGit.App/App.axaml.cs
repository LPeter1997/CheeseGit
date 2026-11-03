using System.Reflection;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using CheeseGit.App.Theming;
using CheeseGit.App.ViewModels;
using CheeseGit.Core.Git;

namespace CheeseGit.App;

public partial class App : Application
{
    public ThemeManager Themes { get; private set; } = null!;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        this.Themes = new ThemeManager(this, Program.Store.Settings.Theme);
        this.Resources["MonoFont"] = Fonts.Mono;

    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (this.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var version = AppVersion();
            // Snapshots never touch the network.
            Services.IUpdateService updates = (Program.SnapshotPath, Program.SnapshotDialog) switch
            {
                (null, _) => Services.ReleaseSource.Create(version),
                // A local update source makes the real updater testable without a window (docs/release.md).
                (_, "update-on-exit") when Services.ReleaseSource.Override is not null => Services.ReleaseSource.Create(version),
                (_, "update") => new Services.SnapshotUpdateService(Services.UpdateMode.InApp),
                (_, "update-notice") => new Services.SnapshotUpdateService(Services.UpdateMode.NoticeOnly),
                _ => new Services.NoUpdateService(),
            };
            var vm = new MainViewModel(Program.Store, this.Themes, new GitRunner(), version, updates);
            // Only this app's own executable can be the helper (not "dotnet" running the dll).
            if (Environment.ProcessPath is { } exe && Path.GetFileNameWithoutExtension(exe).Equals("CheeseGit", StringComparison.OrdinalIgnoreCase))
            {
                vm.EnableAskpass(exe);
            }
            desktop.MainWindow = new MainWindow { DataContext = vm };
        }
        base.OnFrameworkInitializationCompleted();
    }

    private static string AppVersion() =>
        typeof(App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "dev build";
}
