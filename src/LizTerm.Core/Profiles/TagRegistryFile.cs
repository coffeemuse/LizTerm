// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.Core.Profiles;

/// <summary>The on-disk shape of tags.json. An array of entries rather than a flat name-to-colour map, so a tag
/// can gain a field later without reshaping the file. Positional with a default on every parameter, as
/// SessionProfile and AppSettings are, so a key missing from a file reads as its default.</summary>
public sealed record TagRegistryFile(IReadOnlyList<TagEntry>? Tags = null);

/// <summary>One definition as written. <paramref name="Color"/> is a STRING rather than a
/// <see cref="TagColor"/>: JsonStringEnumConverter throws on a name it does not know, which would cost the
/// whole file for one typo, where reading it as text lets <c>TagRegistryStore.Load</c> drop that entry alone
/// and let reconciliation give the tag a fresh colour.</summary>
public sealed record TagEntry(string Name = "", string Color = "");
