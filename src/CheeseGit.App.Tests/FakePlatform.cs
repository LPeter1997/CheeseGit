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

    /// <summary>The last file "saved", by suggested name.</summary>
    public (string Name, byte[] Content)? Saved { get; private set; }

    public Task<bool> SaveFileAsync(string title, string suggestedName, byte[] content)
    {
        this.Saved = (suggestedName, content);
        return Task.FromResult(true);
    }

    /// <summary>A 1×1 PNG, with a marker byte saying whether it was anonymized.</summary>
    public Task<byte[]?> CaptureWindowAsync(Core.Diagnostics.Anonymizer? anonymizer) =>
        Task.FromResult<byte[]?>([.. OnePixelPng, (byte)(anonymizer is null ? 0 : 1)]);

    internal static readonly byte[] OnePixelPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    public Task OpenUrlAsync(string url) => Task.CompletedTask;
}
