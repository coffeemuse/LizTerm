// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Text.Json.Serialization;

namespace LizTerm.Core.Profiles;

/// <summary>Its own context, one per file, as TagRegistryJsonContext is.</summary>
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(RecentHostsFile))]
internal partial class RecentHostsJsonContext : JsonSerializerContext;
