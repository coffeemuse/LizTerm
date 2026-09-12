// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.Core.Profiles;

/// <summary>One tag's definition, which exists independently of any profile that uses it. Profiles carry tag
/// NAMES; this is where the colour lives, so a tag has one colour everywhere rather than one per profile.</summary>
/// <param name="Name">Stored trimmed and without a leading '#', as <c>TagSet.Normalize</c> produces. Compared
/// ignoring case throughout, so <c>prod</c> and <c>PROD</c> are one tag.</param>
public sealed record TagDefinition(string Name, TagColor Color);
