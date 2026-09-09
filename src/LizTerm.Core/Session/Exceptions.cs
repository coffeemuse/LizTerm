// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.Core.Session;

/// <summary>The host connection could not be established; Lines is the emulator's explanation.
/// <see cref="CertificateVerificationFailed"/> is true when the only obstacle was an unverifiable host
/// certificate, so the caller can offer to connect without verifying.</summary>
public sealed class ConnectionFailedException(IReadOnlyList<string> lines, bool certificateVerificationFailed = false)
    : Exception(string.Join(" ", lines))
{
    public IReadOnlyList<string> Lines { get; } = lines;
    public bool CertificateVerificationFailed { get; } = certificateVerificationFailed;
}

/// <summary>An emulator action was rejected (for example, a key while the keyboard is locked).</summary>
public sealed class EmulatorActionException(string message) : Exception(message);

/// <summary>The emulator engine is missing, not executable, or too old.</summary>
public sealed class BackendUnavailableException(string message) : Exception(message);
