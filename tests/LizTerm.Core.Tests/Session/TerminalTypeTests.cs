// This file is part of LizTerm.
// Copyright 2026 by CoffeeMuse
// SPDX-License-Identifier: BSD-3-Clause

using LizTerm.Core.Session;

namespace LizTerm.Core.Tests.Session;

public class TerminalTypeTests
{
    [Fact]
    public void Extended_profiles_carry_the_E_suffix()
    {
        Assert.Equal("3279-2-E", TerminalType.For(new SessionProfile { Model = 2, Extended = true }));
        Assert.Equal("3279-5-E", TerminalType.For(new SessionProfile { Model = 5, Extended = true }));
    }

    [Fact]
    public void Non_extended_profiles_do_not()
    {
        Assert.Equal("3279-4", TerminalType.For(new SessionProfile { Model = 4, Extended = false }));
    }

    /// <summary>A mono profile is a 3278, with or without the extended data stream: b3270 accepts 3278-n-E, and
    /// the host sees IBM-3278-n-E and leaves colour out of its capability reply (#123).</summary>
    [Fact]
    public void Mono_profiles_are_a_3278()
    {
        Assert.Equal("3278-2-E", TerminalType.For(new SessionProfile { Model = 2, Extended = true, Display = TerminalDisplay.Mono }));
        Assert.Equal("3278-3", TerminalType.For(new SessionProfile { Model = 3, Extended = false, Display = TerminalDisplay.Mono }));
    }

    [Fact]
    public void The_default_display_is_colour()
    {
        Assert.Equal(TerminalDisplay.Color, new SessionProfile().Display);
    }
}
