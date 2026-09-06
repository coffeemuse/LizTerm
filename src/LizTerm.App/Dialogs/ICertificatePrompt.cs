namespace LizTerm.App.Dialogs;

/// <summary>Asks whether to connect to a host whose certificate did not verify. Injected like the clipboard so
/// tests answer without a window. A null prompt on the view model declines.</summary>
public interface ICertificatePrompt
{
    Task<CertificateDecision> AskAsync(CertificatePromptRequest request);
}

/// <summary><paramref name="Remember"/> means "pin this certificate for the profile" and is only honoured when the
/// request offered it.</summary>
public sealed record CertificateDecision(bool ConnectAnyway, bool Remember)
{
    public static readonly CertificateDecision Declined = new(false, false);
}
