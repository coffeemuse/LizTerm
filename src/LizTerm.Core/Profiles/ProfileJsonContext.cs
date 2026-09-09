// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using System.Text.Json.Serialization;
using LizTerm.Core.Session;

namespace LizTerm.Core.Profiles;

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(SessionProfile))]
internal partial class ProfileJsonContext : JsonSerializerContext;
