using Avalonia.Controls;
using Avalonia.Input.Platform;

namespace LizTerm.App.Clipboard;

/// <summary>Wraps a top level's clipboard, resolved at each call so the window need not be open yet.
/// A missing clipboard reads as empty and drops writes.</summary>
public sealed class AvaloniaTextClipboard(TopLevel topLevel) : ITextClipboard
{
    public Task SetTextAsync(string text) =>
        topLevel.Clipboard?.SetTextAsync(text) ?? Task.CompletedTask;

    public Task<string?> GetTextAsync() =>
        topLevel.Clipboard?.TryGetTextAsync() ?? Task.FromResult<string?>(null);
}
