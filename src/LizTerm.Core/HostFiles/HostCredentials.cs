// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.Core.HostFiles;

/// <summary>A userid and password held in memory only, the input to a service's <see cref="HostSignIn"/>. A class
/// rather than a record so that no generated <c>ToString</c> can print the password.</summary>
public sealed class HostCredentials(string userid, string password)
{
    public string Userid { get; } = userid;

    public string Password { get; } = password;

    public override string ToString() => $"HostCredentials({Userid}, password hidden)";
}
