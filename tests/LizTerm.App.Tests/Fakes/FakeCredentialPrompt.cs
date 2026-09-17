// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.Dialogs;
using LizTerm.Core.HostFiles;

namespace LizTerm.App.Tests.Fakes;

public sealed class FakeCredentialPrompt : ICredentialPrompt
{
    private readonly object _lock = new();
    private int _askCount;

    /// <summary>Answers in order; when empty, <see cref="Answer"/>. A null answer plays Cancel.</summary>
    public Queue<HostCredentials?> Answers { get; } = new();
    public HostCredentials? Answer { get; set; } = new("MVSCE02", "pw");
    /// <summary>"ask:&lt;userid&gt;:&lt;IsRetry&gt;" per call.</summary>
    public List<string> Calls { get; } = [];
    public CredentialPromptRequest? LastRequest { get; private set; }
    /// <summary>When set, every ask waits for it: a test holds a prompt open to see what else happens meanwhile.</summary>
    public TaskCompletionSource? Gate { get; set; }
    public int AskCount => Volatile.Read(ref _askCount);

    public async Task<HostCredentials?> AskAsync(CredentialPromptRequest request)
    {
        Interlocked.Increment(ref _askCount);
        lock (_lock)
        {
            Calls.Add($"ask:{request.Userid}:{request.IsRetry}");
            LastRequest = request;
        }
        if (Gate is { } gate) await gate.Task;
        lock (_lock) return Answers.Count > 0 ? Answers.Dequeue() : Answer;
    }
}
