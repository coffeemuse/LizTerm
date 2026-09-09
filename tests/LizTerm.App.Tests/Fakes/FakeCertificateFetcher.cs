// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.Security;

namespace LizTerm.App.Tests.Fakes;

public sealed class FakeCertificateFetcher : ICertificateFetcher
{
    public PresentedCertificate Result { get; set; } = new("AA:BB", "CN=fake", "-----BEGIN CERTIFICATE-----\nZmFrZQ==\n-----END CERTIFICATE-----\n", true, null);
    /// <summary>When set, FetchAsync throws it.</summary>
    public Exception? Exception { get; set; }
    /// <summary>"fetch:<host>:<port>" per call.</summary>
    public List<string> Calls { get; } = [];

    public Task<PresentedCertificate> FetchAsync(string host, int port, CancellationToken token)
    {
        Calls.Add($"fetch:{host}:{port}");
        if (Exception is not null) throw Exception;
        return Task.FromResult(Result);
    }
}
