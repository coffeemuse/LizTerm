// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.Core.HostFiles;

/// <summary>An opaque session token held in memory only, traded for a password at sign-in. A class rather than a
/// record so that no generated <c>ToString</c> can print the value.</summary>
public sealed class HostSessionToken(string value)
{
    public string Value { get; } = value;

    public override string ToString() => "HostSessionToken(value hidden)";
}

/// <param name="Rejected">The token the host has just refused (an expired or reaped session), or null on first
/// need. A provider that still holds this instance obtains a new token; one that has moved on answers the newer
/// token.</param>
public sealed record HostTokenRequest(HostSessionToken? Rejected);

/// <summary>A service's own sign-in: credentials in, a session token out. Throws <see cref="HostFileException"/>
/// with <see cref="HostFileErrorKind.Unauthenticated"/> for a refused password and
/// <see cref="HostFileErrorKind.Unsupported"/> for a host with no sign-in service.</summary>
public delegate Task<HostSessionToken> HostSignIn(HostCredentials credentials, CancellationToken cancellationToken);

/// <summary>How a service asks for a session token. It is handed the service's own <paramref name="signIn"/> to call
/// when it needs a fresh one. Answers null when the user cancels. Parallel operations may call it concurrently, so
/// an implementation serialises, and on a request whose <see cref="HostTokenRequest.Rejected"/> is no longer the
/// token it holds, answers the newer token without signing in again.</summary>
public delegate ValueTask<HostSessionToken?> HostTokenProvider(HostTokenRequest request, HostSignIn signIn, CancellationToken cancellationToken);
