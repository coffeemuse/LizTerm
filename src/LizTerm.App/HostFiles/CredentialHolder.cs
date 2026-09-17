// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.Dialogs;
using LizTerm.Core.HostFiles;

namespace LizTerm.App.HostFiles;

/// <summary>The REST sign-in for one session window (spec §3.2): asked once, held in memory, forgotten when the
/// window closes. The only store of the password. Prompts are serialised, so parallel operations that start
/// together, or are refused together, show one prompt between them (the provider contract in Core).</summary>
public sealed class CredentialHolder(string profileName, string url, string? userid)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _lock = new();
    private HostCredentials? _current;
    private string? _lastUserid = userid;
    /// <summary>Cancelled prompts so far. An operation that was already waiting when a prompt was cancelled fails
    /// with it instead of asking again, so one Cancel answers every operation that shared the prompt.</summary>
    private int _cancellations;

    public bool HasCredentials
    {
        get { lock (_lock) return _current is not null; }
    }

    /// <summary>A provider that asks through <paramref name="prompt"/>, which belongs to the window the operation
    /// runs in. Every provider made here shares this holder's pair.</summary>
    public HostCredentialProvider ProviderFor(ICredentialPrompt prompt) => (request, token) => GetAsync(prompt, request, token);

    public void Forget()
    {
        lock (_lock) _current = null;
    }

    private async ValueTask<HostCredentials?> GetAsync(ICredentialPrompt prompt, HostCredentialRequest request, CancellationToken token)
    {
        int cancellationsSeen;
        lock (_lock) cancellationsSeen = _cancellations;
        await _gate.WaitAsync(token);
        try
        {
            string? prefill;
            lock (_lock)
            {
                var refusedIsCurrent = request.IsRetry && (request.Rejected is null || ReferenceEquals(_current, request.Rejected));
                if (_current is { } current && !refusedIsCurrent) return current;
                if (_cancellations != cancellationsSeen && _current is null) return null;
                _current = null;
                prefill = _lastUserid;
            }
            var answer = await prompt.AskAsync(new CredentialPromptRequest(profileName, url, prefill, request.IsRetry));
            if (answer is null)
            {
                lock (_lock) _cancellations++;
                return null;
            }
            lock (_lock)
            {
                _current = answer;
                _lastUserid = answer.Userid;
            }
            return answer;
        }
        finally
        {
            _gate.Release();
        }
    }
}
