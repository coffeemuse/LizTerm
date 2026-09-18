// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.Dialogs;
using LizTerm.Core.HostFiles;

namespace LizTerm.App.HostFiles;

/// <summary>The mvsMF sign-in for one session window (spec §3.2, §4.3): the password is asked for once, traded for a
/// session token through the backend's <see cref="HostSignIn"/>, and dropped; only the token is held, and it is
/// forgotten when the window closes. Prompts are serialised, so parallel operations that start together, or are
/// refused together, show one prompt between them.</summary>
public sealed class SignInHolder(string profileName, string url, string? userid)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _lock = new();
    private HostSessionToken? _token;
    private string? _lastUserid = userid;
    /// <summary>Cancelled prompts so far. An operation that was already waiting when a prompt was cancelled fails
    /// with it instead of asking again, so one Cancel answers every operation that shared the prompt.</summary>
    private int _cancellations;

    public bool IsSignedIn
    {
        get { lock (_lock) return _token is not null; }
    }

    /// <summary>A provider that asks through <paramref name="prompt"/>, which belongs to the window the operation
    /// runs in. Every provider made here shares this holder's token.</summary>
    public HostTokenProvider ProviderFor(ICredentialPrompt prompt) => (request, signIn, token) => GetAsync(prompt, request, signIn, token);

    /// <summary>Ends the session through <paramref name="service"/> if one is held, and drops the token. Any failure
    /// is the service's to swallow (best effort on window close).</summary>
    public async Task SignOutAsync(IHostFileService service)
    {
        if (Take() is { } token) await service.SignOutAsync(token);
    }

    /// <summary>Drops the held token and answers it, or null when nothing is held. Its own step, so a caller that
    /// must end the session on another thread can still clear the holder on this one: <see cref="IsSignedIn"/> is
    /// false the moment the window closes, whatever the network does afterwards.</summary>
    internal HostSessionToken? Take()
    {
        lock (_lock)
        {
            var token = _token;
            _token = null;
            return token;
        }
    }

    private async ValueTask<HostSessionToken?> GetAsync(ICredentialPrompt prompt, HostTokenRequest request, HostSignIn signIn, CancellationToken token)
    {
        int cancellationsSeen;
        lock (_lock) cancellationsSeen = _cancellations;
        await _gate.WaitAsync(token);
        try
        {
            lock (_lock)
            {
                var refusedIsCurrent = request.Rejected is not null && ReferenceEquals(_token, request.Rejected);
                if (_token is { } current && !refusedIsCurrent) return current;
                if (_cancellations != cancellationsSeen && _token is null) return null;
                _token = null;
            }
            // A refused password comes back from signIn, not from the caller, so the loop owns the reason: the
            // first ask blames nothing or the expired session, and every ask after a refusal blames the password.
            var reason = request.Rejected is not null ? SignInReason.Expired : SignInReason.First;
            while (true)
            {
                // Safe outside the lock above: the gate is held, so nothing else is writing the userid.
                string? prefill;
                lock (_lock) prefill = _lastUserid;
                var answer = await prompt.AskAsync(new CredentialPromptRequest(profileName, url, prefill, reason));
                if (answer is null)
                {
                    lock (_lock) _cancellations++;
                    return null;
                }
                // Remembered as soon as it is typed, not once the host takes it: a sign-in that fails for anything
                // else (an untrusted certificate, an unreachable host) must still prefill what the user just typed.
                lock (_lock) _lastUserid = answer.Userid;
                try
                {
                    var signedIn = await signIn(answer, token);
                    lock (_lock) _token = signedIn;
                    return signedIn;
                }
                catch (HostFileException ex) when (ex.Kind == HostFileErrorKind.Unauthenticated)
                {
                    reason = SignInReason.Rejected;
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }
}
