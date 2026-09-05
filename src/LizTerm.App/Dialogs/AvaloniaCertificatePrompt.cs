using Avalonia.Controls;
using LizTerm.App.Views;

namespace LizTerm.App.Dialogs;

/// <summary>Opens <see cref="CertificateWindow"/> modally over the session window.</summary>
public sealed class AvaloniaCertificatePrompt(Window owner) : ICertificatePrompt
{
    public async Task<CertificateDecision> AskAsync(string host, IReadOnlyList<string> reason, bool canRemember)
    {
        var result = await new CertificateWindow(host, reason, canRemember).ShowDialog<CertificateDecision?>(owner);
        return result ?? CertificateDecision.Declined;
    }
}
