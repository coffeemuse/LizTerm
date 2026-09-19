// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.App.Keyboard;

/// <summary>What a Keyboard tab row answers the Add slot when it offers a captured chord (spec §5.3): Accepted means
/// the chord was bound (the slot disarms), otherwise it was refused and <paramref name="Message"/> is the reason,
/// shown verbatim with the slot still armed. An accepted result may carry a note ("Moved from PA2").</summary>
public sealed record CaptureResult(bool Accepted, string? Message);
