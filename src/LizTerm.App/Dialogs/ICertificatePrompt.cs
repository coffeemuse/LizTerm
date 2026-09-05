namespace LizTerm.App.Dialogs;

/// <summary>Asks whether to connect without verifying the host certificate. Injected like the clipboard so tests
/// answer without a window. A null prompt on the view model declines.</summary>
public interface ICertificatePrompt
{
    /// <param name="reason">The engine's explanation, one line each, without the leading "Connection failed:".</param>
    /// <param name="canRemember">Whether "Always allow for this profile" can be offered (the profile is saved).</param>
    Task<CertificateDecision> AskAsync(string host, IReadOnlyList<string> reason, bool canRemember);
}

public sealed record CertificateDecision(bool ConnectAnyway, bool Remember)
{
    public static readonly CertificateDecision Declined = new(false, false);
}
