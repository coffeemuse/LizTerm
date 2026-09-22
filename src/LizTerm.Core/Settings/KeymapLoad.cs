// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.Core.Settings;

/// <summary>What <see cref="KeymapStore.Load"/> found (#168). A load never throws and never writes, so a file this
/// build cannot use still yields <paramref name="File"/> — the empty file, every default in force — and says why in
/// <paramref name="Problem"/>.</summary>
/// <param name="File">The bindings to use. Empty whenever there is a problem.</param>
/// <param name="Problem">Sentences naming the file and what is wrong with it, for the user to act on, or null when
/// the file read — which includes a file that is simply absent, or one holding no "bindings" at all. The caller
/// adds what it means for them; this says only what the file is.</param>
public sealed record KeymapLoad(KeymapFile File, string? Problem)
{
    public static KeymapLoad Empty { get; } = new(KeymapFile.Empty, null);
}
