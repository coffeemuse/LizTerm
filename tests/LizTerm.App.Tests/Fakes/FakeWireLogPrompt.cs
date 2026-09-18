// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.Dialogs;

namespace LizTerm.App.Tests.Fakes;

public sealed class FakeWireLogPrompt : IWireLogPrompt
{
    public bool Confirm { get; set; }
    /// <summary>"confirm:&lt;profile&gt;" per call, so a test can assert the prompt was asked exactly once.</summary>
    public List<string> Calls { get; } = [];
    public WireLogPromptRequest? LastRequest { get; private set; }

    public Task<bool> ConfirmAsync(WireLogPromptRequest request)
    {
        Calls.Add($"confirm:{request.ProfileName}");
        LastRequest = request;
        return Task.FromResult(Confirm);
    }
}
