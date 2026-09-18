// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Runtime.ExceptionServices;
using LizTerm.App.Dialogs;
using LizTerm.Core.HostFiles;

namespace LizTerm.App.HostFiles;

/// <summary>The mvsMF sign-in for one session window (spec §3.2, §4.3): the password is asked for once, traded for a
/// session token through the backend's <see cref="HostSignIn"/>, and dropped; only the token is held, and it is
/// forgotten when the window closes. Prompts are serialised, so parallel operations that start together, or are
/// refused together, show one prompt between them, and share its outcome: a Cancel, or a sign-in that fails for
/// anything but the password, answers every operation that was waiting on that prompt.</summary>
public sealed class SignInHolder(string profileName, string url, string? userid)
{
    /// <summary>How long a sign-out may take before the DELETE is given up on: only cancelling the request itself
    /// stops one to a host that has stopped answering, which would otherwise run on to the backend's idle timeout.
    /// </summary>
    internal static readonly TimeSpan SignOutCap = TimeSpan.FromSeconds(5);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _lock = new();
    private HostSessionToken? _token;
    private string? _lastUserid = userid;
    /// <summary>Prompts so far that ended without a token: cancelled, or failed at sign-in for anything but the
    /// password. An operation that was already waiting when one ended fails with it (<see cref="_lastFailure"/>, or
    /// null for a Cancel) instead of asking again, so one prompt answers every operation that shared it.</summary>
    private int _ended;
    private HostFileException? _lastFailure;

    public bool IsSignedIn
    {
        get { lock (_lock) return _token is not null; }
    }

    /// <summary>A provider that asks through <paramref name="prompt"/>, which belongs to the window the operation
    /// runs in. Every provider made here shares this holder's token.</summary>
    public HostTokenProvider ProviderFor(ICredentialPrompt prompt) => (request, signIn, token) => GetAsync(prompt, request, signIn, token);

    /// <summary>Ends the session through <paramref name="service"/> if one is held, and drops the token. Any failure
    /// is the service's to swallow (best effort on window close); <paramref name="cancellationToken"/> is the
    /// caller's own give-up, which the service lets through.</summary>
    public async Task SignOutAsync(IHostFileService service, CancellationToken cancellationToken = default)
    {
        if (Take() is { } token) await EndAsync(service, token, cancellationToken);
    }

    /// <summary>Ends the session <paramref name="token"/> names, giving up after <paramref name="cap"/> (default
    /// <see cref="SignOutCap"/>). The cap's own cancellation is swallowed here, so every caller gets the same bound
    /// without building one; the caller's <paramref name="cancellationToken"/> still propagates.</summary>
    internal static async Task EndAsync(IHostFileService service, HostSessionToken token, CancellationToken cancellationToken, TimeSpan? cap = null)
    {
        using var capped = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        capped.CancelAfter(cap ?? SignOutCap);
        try
        {
            await service.SignOutAsync(token, capped.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // The cap: the DELETE was given up on, and there is nothing more to do about it.
        }
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
        int endedSeen;
        lock (_lock) endedSeen = _ended;
        await _gate.WaitAsync(token);
        try
        {
            HostFileException? sharedFailure;
            lock (_lock)
            {
                var refusedIsCurrent = request.Rejected is not null && ReferenceEquals(_token, request.Rejected);
                if (_token is { } current && !refusedIsCurrent) return current;
                if (_ended != endedSeen && _token is null)
                {
                    if (_lastFailure is null) return null;
                    sharedFailure = _lastFailure;
                }
                else
                {
                    sharedFailure = null;
                    _token = null;
                }
            }
            // The prompt this operation was waiting on ended in a failed sign-in: fail the same way, as the
            // operation that asked did, rather than ask again for a password that was just typed.
            if (sharedFailure is not null) ExceptionDispatchInfo.Throw(sharedFailure);
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
                    lock (_lock)
                    {
                        _ended++;
                        _lastFailure = null;
                    }
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
                catch (HostFileException ex)
                {
                    // Unreachable, an untrusted certificate, no sign-in route: the prompt is over, and whoever was
                    // waiting on it fails with this too.
                    lock (_lock)
                    {
                        _ended++;
                        _lastFailure = ex;
                    }
                    throw;
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }
}
