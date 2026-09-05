using LizTerm.App.Dialogs;

namespace LizTerm.App.Tests.Fakes;

public sealed class FakeCertificatePrompt : ICertificatePrompt
{
    public CertificateDecision Decision { get; set; } = CertificateDecision.Declined;
    /// <summary>Runs before the decision is returned; tests use it to change the fake session between attempts.</summary>
    public Action? OnAsk { get; set; }
    /// <summary>"ask:<host>:<canRemember>" per call.</summary>
    public List<string> Calls { get; } = [];
    public IReadOnlyList<string>? LastReason { get; private set; }

    public Task<CertificateDecision> AskAsync(string host, IReadOnlyList<string> reason, bool canRemember)
    {
        Calls.Add($"ask:{host}:{canRemember}");
        LastReason = reason;
        OnAsk?.Invoke();
        return Task.FromResult(Decision);
    }
}
