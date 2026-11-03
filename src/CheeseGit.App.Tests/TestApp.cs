using Avalonia;
using Avalonia.Headless;
using CheeseGit.App;
using CheeseGit.App.Services;
using CheeseGit.Core.Tests;

[assembly: AvaloniaTestApplication(typeof(CheeseGit.App.Tests.TestApp))]

// One Application and Dispatcher for all tests. Recreating them per test (the
// default) failed about once in ten runs once the suite grew: a new app's
// compositor found the render loop owned by the previous dispatcher's thread
// ("The calling thread cannot access this object"). Tests share no app state:
// each builds its own MainViewModel with in-memory settings.
[assembly: AvaloniaTestIsolation(AvaloniaTestIsolationLevel.PerAssembly)]

namespace CheeseGit.App.Tests;

/// <summary>The real <see cref="App"/> (styles, themes, templates) on the headless platform, drawing with Skia.</summary>
public static class TestApp
{
    public static AppBuilder BuildAvaloniaApp()
    {
        TestEnvironment.EnsureInitialized();
        // Never the user's settings file.
        Program.Store = SettingsStore.InMemory();
        return AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .WithInterFont();
    }
}
