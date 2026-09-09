// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.Security;
using LizTerm.Core.Session;

namespace LizTerm.App.Dialogs;

/// <summary>Everything the certificate prompt shows (spec 5.2).</summary>
/// <param name="Reason">The engine's lines, without the leading "Connection failed:".</param>
/// <param name="Presented">What the host presented, or null when the fetch failed or was not attempted.</param>
/// <param name="FetchError">The fetch exception's message when <paramref name="Presented"/> is null for that reason.</param>
/// <param name="Previous">The pin in force when the failure happened; non-null means "certificate changed".</param>
/// <param name="CanPin">Whether "Trust this certificate for this profile" is offered.</param>
/// <param name="CannotPinReason">Shown, instead of the checkbox, when a saved TLS profile cannot pin this certificate.</param>
public sealed record CertificatePromptRequest(
    string Host,
    IReadOnlyList<string> Reason,
    PresentedCertificate? Presented,
    string? FetchError,
    CertificatePin? Previous,
    bool CanPin,
    string? CannotPinReason);
