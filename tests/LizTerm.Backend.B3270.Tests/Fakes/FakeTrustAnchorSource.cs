// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.Security;

namespace LizTerm.Backend.B3270.Tests.Fakes;

/// <summary>A trust source a test controls. Two PEM blocks by default, so a test can tell the roots file apart from
/// a pin file by content as well as by name.</summary>
internal sealed class FakeTrustAnchorSource : ITrustAnchorSource
{
    public const string TwoRoots =
        "-----BEGIN CERTIFICATE-----\ncm9vdDE=\n-----END CERTIFICATE-----\n" +
        "-----BEGIN CERTIFICATE-----\ncm9vdDI=\n-----END CERTIFICATE-----\n";

    public string? Pem { get; set; } = TwoRoots;

    /// <summary>How many times a connect asked for anchors, so a test can assert one never did.</summary>
    public int Calls { get; private set; }

    public string? ExportPem()
    {
        Calls++;
        return Pem;
    }
}
