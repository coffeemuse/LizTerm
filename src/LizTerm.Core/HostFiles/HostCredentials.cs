// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.Core.HostFiles;

/// <summary>A userid and password held in memory only. A class rather than a record so that no generated
/// <c>ToString</c> can print the password.</summary>
public sealed class HostCredentials(string userid, string password)
{
    public string Userid { get; } = userid;

    public string Password { get; } = password;

    public override string ToString() => $"HostCredentials({Userid}, password hidden)";
}

/// <param name="IsRetry">True when the host has just rejected the credentials the provider last gave, so a cached
/// pair must be dropped and the user asked again.</param>
/// <param name="Rejected">The exact instance the host refused; set when <paramref name="IsRetry"/> is true.</param>
public sealed record HostCredentialRequest(bool IsRetry, HostCredentials? Rejected = null);

/// <summary>How a service asks for credentials. Answers null when the user cancels. Parallel operations may call it
/// concurrently, so an implementation should serialise its prompts and, on a retry, prompt again only if its current
/// credentials are still the <see cref="HostCredentialRequest.Rejected"/> instance; otherwise another operation has
/// already replaced them, and it should answer with the newer pair.</summary>
public delegate ValueTask<HostCredentials?> HostCredentialProvider(HostCredentialRequest request, CancellationToken cancellationToken);
