// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.Core.HostFiles;

/// <summary>What a file service reports about itself, for a connection test.</summary>
public sealed record HostServerInfo(string Product, string ProductVersion, string SystemVersion);
