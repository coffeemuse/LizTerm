// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.HostFiles;

namespace LizTerm.App.Dialogs;

/// <summary>Asks for the REST sign-in. Injected like <see cref="ICertificatePrompt"/> so tests answer without a
/// window. Null means the user cancelled.</summary>
public interface ICredentialPrompt
{
    Task<HostCredentials?> AskAsync(CredentialPromptRequest request);
}

/// <summary>Why the sign-in window is open.</summary>
public enum SignInReason
{
    First,
    Rejected,
    Expired,
}

/// <param name="ProfileName">Whose sign-in this is.</param>
/// <param name="Url">The REST base URL, shown so the user knows which host is asking.</param>
/// <param name="Userid">The userid to start with, or null.</param>
/// <param name="Reason">First need, a refused password, or an expired session.</param>
public sealed record CredentialPromptRequest(string ProfileName, string Url, string? Userid, SignInReason Reason);
