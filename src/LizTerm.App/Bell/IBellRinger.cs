// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.Settings;

namespace LizTerm.App.Bell;

/// <summary>Makes the bell audible. Injected into SessionViewModel like the clipboard and the prompts, so the
/// platform calls stay out of the view model and "the host rang and we responded" is assertable without a
/// speaker. Called on the UI thread, and never with BellSound.None: "None means silence" is the view model's rule,
/// and a ringer only knows how to make sounds. Taking the sound rather than being parameterless is the hook for a
/// user-chosen file later (bell spec §3.3).</summary>
public interface IBellRinger
{
    void Ring(BellSound sound);
}
