using Avalonia.Controls;
using LizTerm.App.Views;

namespace LizTerm.App.Dialogs;

/// <summary>Opens <see cref="CertificateWindow"/> modally over the session window.</summary>
public sealed class AvaloniaCertificatePrompt(Window owner) : ICertificatePrompt
{
    public async Task<CertificateDecision> AskAsync(CertificatePromptRequest request)
    {
        var result = await new CertificateWindow(request.Host, request.Reason, request.CanPin).ShowDialog<CertificateDecision?>(owner);
        return result ?? CertificateDecision.Declined;
    }
}
