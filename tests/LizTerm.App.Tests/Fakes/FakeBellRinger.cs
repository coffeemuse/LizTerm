// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.App.Bell;
using LizTerm.Core.Settings;

namespace LizTerm.App.Tests.Fakes;

public sealed class FakeBellRinger : IBellRinger
{
    public List<BellSound> Rings { get; } = [];
    /// <summary>When set, Ring throws it after recording the call.</summary>
    public Exception? Exception { get; set; }
    /// <summary>What CanRing answers for every sound; false stands for a platform that cannot ring.</summary>
    public bool Available { get; set; } = true;

    public bool CanRing(BellSound sound) => Available;

    public void Ring(BellSound sound)
    {
        Rings.Add(sound);
        if (Exception is not null) throw Exception;
    }
}
