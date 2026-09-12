// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Text.Json.Serialization;

namespace LizTerm.Core.Profiles;

/// <summary>Its own context, not ProfileJsonContext's: one context per file, as SettingsJsonContext is separate
/// from ProfileJsonContext.</summary>
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(TagRegistryFile))]
internal partial class TagRegistryJsonContext : JsonSerializerContext;
