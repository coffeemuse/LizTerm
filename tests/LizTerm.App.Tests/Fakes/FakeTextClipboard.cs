using LizTerm.App.Clipboard;

namespace LizTerm.App.Tests.Fakes;

public sealed class FakeTextClipboard : ITextClipboard
{
    public string? Text { get; set; }

    /// <summary>When set, both operations fail with this exception.</summary>
    public Exception? Exception { get; set; }

    public Task SetTextAsync(string text)
    {
        if (Exception is not null) return Task.FromException(Exception);
        Text = text;
        return Task.CompletedTask;
    }

    public Task<string?> GetTextAsync() =>
        Exception is not null ? Task.FromException<string?>(Exception) : Task.FromResult(Text);
}
