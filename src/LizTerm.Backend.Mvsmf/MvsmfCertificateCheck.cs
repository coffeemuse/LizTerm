// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using LizTerm.Core.Security;
using LizTerm.Core.Session;

namespace LizTerm.Backend.Mvsmf;

internal sealed class MvsmfCertificateCheck(CertificatePin? pin)
{
    public CertificatePin? Pin { get; } = pin;

    public PresentedCertificate? LastRejected => null;

    public bool Validate(object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors errors) =>
        errors == SslPolicyErrors.None;
}
