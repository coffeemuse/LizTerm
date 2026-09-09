// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.Dialogs;

namespace LizTerm.App.Tests.Fakes;

public sealed class FakeCertificatePrompt : ICertificatePrompt
{
    public CertificateDecision Decision { get; set; } = CertificateDecision.Declined;
    /// <summary>Runs before the decision is returned; tests use it to change the fake session between attempts.</summary>
    public Action? OnAsk { get; set; }
    /// <summary>"ask:<host>:<canPin>" per call.</summary>
    public List<string> Calls { get; } = [];
    public CertificatePromptRequest? LastRequest { get; private set; }
    public IReadOnlyList<string>? LastReason => LastRequest?.Reason;
    /// <summary>When set, AskAsync throws it, simulating ShowDialog over an owner that has gone away.</summary>
    public Exception? AskException { get; set; }

    public Task<CertificateDecision> AskAsync(CertificatePromptRequest request)
    {
        Calls.Add($"ask:{request.Host}:{request.CanPin}");
        LastRequest = request;
        OnAsk?.Invoke();
        if (AskException is not null) throw AskException;
        return Task.FromResult(Decision);
    }
}
