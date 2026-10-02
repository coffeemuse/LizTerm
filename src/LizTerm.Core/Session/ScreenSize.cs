// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

namespace LizTerm.Core.Session;

/// <summary>A screen's rows and columns.</summary>
public readonly record struct ScreenSize(int Rows, int Columns)
{
    /// <summary>The largest screen a session for <paramref name="profile"/> can show: the oversize geometry the
    /// engine will use (<see cref="OversizeGeometry.TryFor"/>), else the model's own size. That is the model's
    /// alternate screen, which the host switches to with Erase/Write Alternate; the default screen it starts on is
    /// 24x80 for every model. b3270 reports the same pair as screen-mode's rows and columns (x3270's maxROWS and
    /// maxCOLS), but the window needs it before the engine runs (#198). A model outside the catalogue reads as
    /// 24x80.</summary>
    public static ScreenSize AlternateFor(SessionProfile profile)
    {
        if (OversizeGeometry.TryFor(profile, out var oversize, out _) && oversize is not null)
            return new ScreenSize(oversize.Rows, oversize.Columns);
        var model = TerminalModel.Find(profile.Model);
        return model is null ? new ScreenSize(24, 80) : new ScreenSize(model.Rows, model.Columns);
    }
}
