// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.Core.Session;

/// <summary>Where the emulator engine binary came from: shipped inside the app (a runtimes folder or an app
/// bundle), or pointed to from outside it (today an environment variable; a preference later).</summary>
public enum EngineSource
{
    /// <summary>Never located. The default, so a half-built <see cref="EngineInfo"/> claims no provenance.</summary>
    Unknown,
    Bundled,
    Override,
}

/// <summary>The emulator engine binary a session uses. <see cref="Version"/> is null until the engine has started.</summary>
public sealed record EngineInfo(string Name, string? Version, string Path, EngineSource Source);
