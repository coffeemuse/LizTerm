// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.Core.Session;

public sealed record TlsInfo(bool Secure, bool? Verified, string? SessionInfo, string? HostCertificate);
