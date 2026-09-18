// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.Dialogs;
using LizTerm.Core.HostFiles;

namespace LizTerm.App.HostFiles;

/// <summary>Used only for sign-out, where a token is already held so the prompt is never reached.</summary>
internal sealed class NullPrompt : ICredentialPrompt
{
    public static readonly NullPrompt Instance = new();

    public Task<HostCredentials?> AskAsync(CredentialPromptRequest request) =>
        throw new InvalidOperationException("Sign-out must not prompt.");
}
