// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.Dialogs;

namespace LizTerm.App.Tests.Fakes;

public sealed class FakeClosePrompt : IClosePrompt
{
    /// <summary>What the user answers: true is Disconnect, false is Keep Connected.</summary>
    public bool Disconnect { get; set; }
    /// <summary>"confirm:&lt;title&gt;" per call, so a test can assert the prompt was asked exactly once.</summary>
    public List<string> Calls { get; } = [];
    public ClosePromptRequest? LastRequest { get; private set; }
    /// <summary>When set, every ask waits for it: a test holds the prompt open to see what a second close does.</summary>
    public TaskCompletionSource? Gate { get; set; }

    public async Task<bool> ConfirmAsync(ClosePromptRequest request)
    {
        Calls.Add($"confirm:{request.Title}");
        LastRequest = request;
        if (Gate is { } gate) await gate.Task;
        return Disconnect;
    }
}
