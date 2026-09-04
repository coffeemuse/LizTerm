namespace LizTerm.App.Clipboard;

/// <summary>Plain-text clipboard access, injected into the view model so tests can fake it.</summary>
public interface ITextClipboard
{
    Task SetTextAsync(string text);

    /// <summary>Null when the clipboard holds no text.</summary>
    Task<string?> GetTextAsync();
}
