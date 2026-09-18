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
        HostSessionToken? token;
        lock (_lock) { token = _token; _token = null; }
        if (token is not null) await service.SignOutAsync(token);
    }

    private async ValueTask<HostSessionToken?> GetAsync(ICredentialPrompt prompt, HostTokenRequest request, HostSignIn signIn, CancellationToken token)
    {
        int cancellationsSeen;
        lock (_lock) cancellationsSeen = _cancellations;
        await _gate.WaitAsync(token);
        try
        {
            string? prefill;
            lock (_lock)
            {
                var refusedIsCurrent = request.Rejected is not null && ReferenceEquals(_token, request.Rejected);
                if (_token is { } current && !refusedIsCurrent) return current;
                if (_cancellations != cancellationsSeen && _token is null) return null;
                _token = null;
                prefill = _lastUserid;
            }
            var reason = request.Rejected is not null ? SignInReason.Expired : SignInReason.First;
            var answer = await prompt.AskAsync(new CredentialPromptRequest(profileName, url, prefill, reason));
            if (answer is null)
            {
                lock (_lock) _cancellations++;
                return null;
            }
            HostSessionToken signedIn;
            try
            {
                signedIn = await signIn(answer, token);
            }
            catch (HostFileException ex) when (ex.Kind == HostFileErrorKind.Unauthenticated)
            {
                // The host refused the password: ask again, marked as a rejection, until it takes or the user cancels.
                return await RetryAfterRejection(prompt, signIn, token);
            }
            lock (_lock)
            {
                _token = signedIn;
                _lastUserid = answer.Userid;
            }
            return signedIn;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async ValueTask<HostSessionToken?> RetryAfterRejection(ICredentialPrompt prompt, HostSignIn signIn, CancellationToken token)
    {
        while (true)
        {
            string? prefill;
            lock (_lock) prefill = _lastUserid;
            var answer = await prompt.AskAsync(new CredentialPromptRequest(profileName, url, prefill, SignInReason.Rejected));
            if (answer is null)
            {
                lock (_lock) _cancellations++;
                return null;
            }
            try
            {
                var signedIn = await signIn(answer, token);
                lock (_lock) { _token = signedIn; _lastUserid = answer.Userid; }
                return signedIn;
            }
            catch (HostFileException ex) when (ex.Kind == HostFileErrorKind.Unauthenticated)
            {
                // Ask again.
            }
        }
    }
}
