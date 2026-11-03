using CheeseGit.App.ViewModels;

namespace CheeseGit.App.Tests;

/// <summary>Platform services that record the clipboard instead of touching the desktop's.</summary>
internal sealed class FakePlatform : IPlatformServices
{
    public string? Clipboard { get; private set; }

    public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);

    public Task SetClipboardTextAsync(string text)
    {
        this.Clipboard = text;
        return Task.CompletedTask;
    }

    public Task SaveTextFileAsync(string title, string suggestedName, string text) => Task.CompletedTask;

    public Task OpenUrlAsync(string url) => Task.CompletedTask;
}
