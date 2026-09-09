// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.Core.Session;

/// <summary>A host certificate the user chose to trust for a profile (spec 3.1). <paramref name="Pem"/> holds
/// every certificate the host presented, leaf first, as concatenated PEM blocks, and is what the engine verifies
/// against; <paramref name="Sha256"/> is the leaf's fingerprint as colon-separated upper-case hex pairs, the way
/// openssl prints it; <paramref name="Subject"/> is the leaf's subject, for display.</summary>
public sealed record CertificatePin(string Sha256, string Subject, string Pem);
